using System.Drawing;

namespace Server.RuneSystem
{
    /// <summary>
    /// 一次"待补发的法术"（双发类符文用）。
    ///
    /// 为什么需要它：一次施法只能走一遍原版流程（消耗一张符、进一次 CompleteMagic），
    /// 想再发一张就必须等第一发的动作播完（客户端的投射物是在施法动作最后一帧生成的），
    /// 所以这里只登记"什么时候、对谁、打多少"，由 RuneEvents.OnTick 到期执行。
    /// </summary>
    internal sealed class RunePendingCast
    {
        /// <summary>到期的服务器时间（Envir.Time）。</summary>
        public long DueTime;

        /// <summary>要补发的技能（目前只用于 SoulFireBall 这类"飞出去再结算"的法术）。</summary>
        public Spell Spell;

        /// <summary>目标对象 Id（到期时重新查，避免拿着已经死掉的对象引用）。</summary>
        public uint TargetId;

        /// <summary>目标当时的格子（客户端投射物需要落点）。</summary>
        public Point TargetLocation;

        /// <summary>这一发的伤害（与第一发同源，避免重复掷骰导致两发差太多）。</summary>
        public int Damage;

        /// <summary>触发它的符文（用于下发给客户端做表现，如特效缩放）。</summary>
        public ushort RuneId;
    }
}
