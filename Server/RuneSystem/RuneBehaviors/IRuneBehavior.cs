namespace Server.RuneSystem
{
    /// <summary>符文可以接管的战斗时机。新增时机 = 在 RuneEvents 里加一行门面调用。</summary>
    public enum BehaviorEvent
    {
        Swing = 0,
        DealtDamage = 1,
        Cast = 2,
        MagicComplete = 3,
    }

    /// <summary>
    /// 符文行为插件接口。所有方法都有默认实现，按需覆盖即可。
    /// 新增一种符文机制 = 在 RuneBehaviors/ 里新增一个实现本接口的文件（反射自动注册），
    /// 不需要修改任何现有文件。
    /// </summary>
    public interface IRuneBehavior
    {
        /// <summary>与 Runes.json 里的 BehaviorId 对应（不区分大小写）。</summary>
        string Id { get; }

        /// <summary>每次成功挥砍。</summary>
        void OnSwing(RuneContext ctx) { }

        /// <summary>真正造成伤害。damage 为可读值，需要改写请用 TryCast/OnMagicComplete 链路。</summary>
        void OnDealtDamage(RuneContext ctx, int damage) { }

        /// <summary>返回 true 表示接管本次施法（调用方会跳过原技能实现）。</summary>
        bool OnCast(RuneContext ctx) => false;

        /// <summary>返回 true 表示接管本次结算。</summary>
        bool OnMagicComplete(RuneContext ctx, IList<object> data) => false;
    }
}
