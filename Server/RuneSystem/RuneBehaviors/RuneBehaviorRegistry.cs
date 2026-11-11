using System;
using System.Reflection;

namespace Server.RuneSystem
{
    /// <summary>
    /// 符文行为注册表：启动时反射扫描 IRuneBehavior 的所有实现并缓存。
    /// 新增行为只需要在 RuneBehaviors/ 里丢一个实现文件 —— 不需要登记，也不改任何现有代码。
    /// </summary>
    public static class RuneBehaviorRegistry
    {
        public const string BuffOnSwing = "BuffOnSwing";
        public const string LifestealOnDealtDamage = "LifestealOnDealtDamage";
        public const string GuaranteedCritOnSwing = "GuaranteedCritOnSwing";
        public const string RepeatCastOnSwing = "RepeatCastOnSwing";
        public const string DebuffOnHit = "DebuffOnHit";
        public const string DualPoison = "DualPoison";
        public const string AreaOnHit = "AreaOnHit";
        public const string DoubleCast = "DoubleCast";

        private static readonly Dictionary<string, IRuneBehavior> Behaviors =
            new Dictionary<string, IRuneBehavior>(StringComparer.OrdinalIgnoreCase);

        private static bool _scanned;

        /// <summary>扫描程序集（幂等）。</summary>
        public static void Scan()
        {
            if (_scanned) return;
            _scanned = true;

            try
            {
                foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(IRuneBehavior).IsAssignableFrom(type)) continue;

                    if (Activator.CreateInstance(type) is not IRuneBehavior behavior) continue;
                    if (string.IsNullOrWhiteSpace(behavior.Id)) continue;

                    Behaviors[behavior.Id] = behavior;
                }

                if (Behaviors.Count > 0)
                    MessageQueue.Instance.Enqueue($"[Rune] 已注册 {Behaviors.Count} 个符文行为: {string.Join(", ", Behaviors.Keys)}");
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue(new Exception($"[Rune] 行为扫描失败: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// BehaviorId 支持多个实现，用逗号分隔（"LifestealOnDealtDamage,DebuffOnHit"），
        /// 这样一个符文可以同时拥有多个能力，而不用为每种组合新写一个行为类。
        /// </summary>
        private static string[] SplitIds(string ids)
        {
            return string.IsNullOrWhiteSpace(ids)
                ? Array.Empty<string>()
                : ids.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public static bool Knows(RuneInfo rune)
        {
            if (rune == null) return false;

            var ids = SplitIds(rune.BehaviorId);
            if (ids.Length == 0) return true;

            foreach (var id in ids)
                if (!Behaviors.ContainsKey(id)) return false;

            return true;
        }

        public static bool TryGet(string id, out IRuneBehavior behavior)
        {
            behavior = null;
            if (string.IsNullOrWhiteSpace(id)) return false;

            return Behaviors.TryGetValue(id, out behavior);
        }

        public static void Dispatch(RuneContext ctx, BehaviorEvent evt, IList<object> data = null)
        {
            if (ctx?.Rune == null) return;

            var ids = SplitIds(ctx.Rune.BehaviorId);

            if (ids.Length == 0)
            {
                if (evt == BehaviorEvent.Swing) ApplyGenericBuff(ctx);
                return;
            }

            bool handled = false;

            foreach (var id in ids)
            {
                //未知行为在 RuneRepository.Resolve 加载时已报过一次，这里静默跳过（避免逐刀刷日志）
                if (!Behaviors.TryGetValue(id, out var behavior)) continue;

                ApplyBehavior(behavior, ctx, evt, data);
                handled = true;
            }

            if (!handled && evt == BehaviorEvent.Swing) ApplyGenericBuff(ctx);
        }

        /// <summary>返回 true 表示事件被接管。</summary>
        public static bool DispatchResult(RuneContext ctx, BehaviorEvent evt, IList<object> data = null)
        {
            if (ctx?.Rune == null) return false;

            foreach (var id in SplitIds(ctx.Rune.BehaviorId))
            {
                if (!Behaviors.TryGetValue(id, out var behavior)) continue;

                switch (evt)
                {
                    case BehaviorEvent.Cast:
                        if (behavior.OnCast(ctx)) return true;
                        break;
                    case BehaviorEvent.MagicComplete:
                        if (behavior.OnMagicComplete(ctx, data)) return true;
                        break;
                }
            }

            return false;
        }

        private static void ApplyBehavior(IRuneBehavior behavior, RuneContext ctx, BehaviorEvent evt, IList<object> data)
        {
            switch (evt)
            {
                case BehaviorEvent.Swing:
                    behavior.OnSwing(ctx);
                    break;
                case BehaviorEvent.DealtDamage:
                    behavior.OnDealtDamage(ctx, ctx.Damage);
                    break;
                case BehaviorEvent.Cast:
                    behavior.OnCast(ctx);
                    break;
                case BehaviorEvent.MagicComplete:
                    behavior.OnMagicComplete(ctx, data);
                    break;
            }
        }

        /// <summary>
        /// 没有行为实现时的兜底：只要符文声明了 BuffOnSwing 且配了 Buff，
        /// 直接按数据上 Buff（数据先行、代码后补也能生效）。
        /// </summary>
        private static void ApplyGenericBuff(RuneContext ctx)
        {
            if (ctx.Rune.ResolvedBuffType == BuffType.None) return;

            RuneEffects.ApplyBuff(ctx);
        }
    }
}
