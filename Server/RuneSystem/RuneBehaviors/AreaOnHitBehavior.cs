using System;
using System.Collections.Generic;
using System.Drawing;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.RuneSystem
{
    /// <summary>
    /// 溅射类（符文「灵魂·炎爆」等）：技能对主目标结算时，主目标周围 SplashRadius 格内的其它可攻击目标，
    /// 再各吃一份 SplashPct% 的伤害。
    ///
    /// 挂在 CompleteMagic 阶段：这里能直接读到 GetDamage 掷出来的伤害值（data[1]）和主目标（data[2]），
    /// 所以溅射伤害与主目标完全同源（同样的攻击力、同样的加成），不需要重算。
    /// 返回 false 表示"不接管"，原技能照常结算主目标。
    /// </summary>
    public sealed class AreaOnHitBehavior : IRuneBehavior
    {
        public string Id { get { return RuneBehaviorRegistry.AreaOnHit; } }

        public bool OnMagicComplete(RuneContext ctx, IList<object> data)
        {
            if (ctx.Owner == null || ctx.Rune == null || data == null || data.Count < 3) return false;
            if (ctx.UsedSpell != ctx.Rune.Spell) return false;

            if (!(data[1] is int value) || !(data[2] is MapObject primary)) return false;

            if (primary.Dead || primary.Node == null || primary.CurrentMap != ctx.Owner.CurrentMap) return false;
            if (!primary.IsAttackTarget(ctx.Owner)) return false;

            if (!RuneEffects.TryProc(ctx.Rune)) return false;

            int radius = ctx.Rune.SplashRadius > 0 ? ctx.Rune.SplashRadius : 1;
            if (radius > 5) radius = 5;                     // 防止数据写错把整屏都打了

            int pct = ctx.Rune.SplashPct > 0 ? ctx.Rune.SplashPct : 100;
            int damage = (int)Math.Round(value * pct / 100f);
            if (damage < 1) damage = 1;

            Point center = primary.CurrentLocation;
            Map map = ctx.Owner.CurrentMap;

            for (int x = center.X - radius; x <= center.X + radius; x++)
            {
                for (int y = center.Y - radius; y <= center.Y + radius; y++)
                {
                    if (x == center.X && y == center.Y) continue;   // 主目标由原技能结算，不重复打

                    Cell cell = map.GetCell(new Point(x, y));
                    if (cell == null || cell.Objects == null) continue;

                    // Objects 在 Attacked 过程中可能被改动（死亡/掉落），先拷一份
                    var objects = new List<MapObject>(cell.Objects);

                    for (int i = 0; i < objects.Count; i++)
                    {
                        MapObject ob = objects[i];
                        if (ob == null || ob == primary) continue;
                        if (ob.Dead || ob.Node == null) continue;
                        if (ob.CurrentMap != map) continue;
                        if (!ob.IsAttackTarget(ctx.Owner)) continue;

                        // 与灵魂火符本体一致：走 MAC（魔法防御）判定
                        ob.Attacked(ctx.Owner, damage, DefenceType.MAC, false);
                    }
                }
            }

            return false;
        }
    }
}
