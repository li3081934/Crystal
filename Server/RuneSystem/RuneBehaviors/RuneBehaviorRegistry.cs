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

        public static bool Knows(RuneInfo rune)
        {
            if (rune == null) return false;
            if (string.IsNullOrWhiteSpace(rune.BehaviorId)) return true;

            return Behaviors.ContainsKey(rune.BehaviorId);
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

            if (string.IsNullOrWhiteSpace(ctx.Rune.BehaviorId))
            {
                if (evt == BehaviorEvent.Swing) ApplyGenericBuff(ctx);
                return;
            }

            if (!Behaviors.TryGetValue(ctx.Rune.BehaviorId, out var behavior))
            {
                if (evt == BehaviorEvent.Swing) ApplyGenericBuff(ctx);
                return;
            }

            ApplyBehavior(behavior, ctx, evt, data);
        }

        /// <summary>返回 true 表示事件被接管。</summary>
        public static bool DispatchResult(RuneContext ctx, BehaviorEvent evt, IList<object> data = null)
        {
            if (ctx?.Rune == null) return false;

            if (!string.IsNullOrWhiteSpace(ctx.Rune.BehaviorId) &&
                Behaviors.TryGetValue(ctx.Rune.BehaviorId, out var behavior))
            {
                switch (evt)
                {
                    case BehaviorEvent.Cast:
                        return behavior.OnCast(ctx);
                    case BehaviorEvent.MagicComplete:
                        return behavior.OnMagicComplete(ctx, data);
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
