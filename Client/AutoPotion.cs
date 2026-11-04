using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using C = ClientPackets;

namespace Client
{
    /// <summary>
    /// 客户端内挂：自动喝药
    /// 纯客户端实现，通过发送正常的 C.UseItem 包使用背包/快捷栏中的药水。
    /// 配置项存于 Mir2Config.ini 的 [AutoPot] 段，界面见 AutoPotDialog。
    /// </summary>
    internal static class AutoPotion
    {
        private static long _nextCheck;

        /// <summary>最近一次成功发送喝药的时间（调试/界面显示用）</summary>
        public static long LastHPTime, LastMPTime;

        public static void Reset()
        {
            _nextCheck = 0;
            LastHPTime = 0;
            LastMPTime = 0;
        }

        /// <summary>同一属性两次喝药的最小间隔，防止网络延迟内重复发送浪费药水</summary>
        private const int MinUseInterval = 700;

        public static void Process()
        {
            GameScene scene = GameScene.Scene;
            if (scene == null) return;

            UserObject user = GameScene.User;
            if (user == null || user.Dead) return;

            long now = CMain.Time;
            if (now < _nextCheck) return;
            _nextCheck = now + Settings.AutoPotDelay;

            // 道士治疗术优先（不消耗道具，能省药）
            bool healed = TryHeal(user, now);

            if (!Settings.AutoPotEnabled) return;
            if (user.Inventory == null) return;

            if (!healed) TryAutoUse(user, Stat.HP, now);   // 已经放了治疗术就不用再喝红
            TryAutoUse(user, Stat.MP, now);
        }

        /// <summary>
        /// 道士自动治疗术：血量低于阈值时把 Spell.Healing 交给客户端原有的施法流程。
        /// 只是设置 User.NextMagic 等字段，真正发包/冷却/扣蓝判断都在 GameScene.UseMagic 里，
        /// 所以行为和手动按技能键完全一致。
        /// </summary>
        /// <returns>true = 这一轮已经发起施法（调用方就别再喝红药了）</returns>
        private static bool TryHeal(UserObject user, long now)
        {
            if (!Settings.AutoHealEnabled) return false;
            if (user.Class != MirClass.Taoist) return false;

            int percent = Settings.AutoHealPercent;
            if (percent <= 0) return false;

            int max = user.Stats[Stat.HP];
            if (max <= 0) return false;
            if (user.HP * 100L >= (long)max * percent) return false;   // 血量还够

            if (user.NextMagic != null) return false;                  // 正在施法
            if (user.RidingMount) return false;                        // 骑马不能放
            if (CMain.Time < GameScene.SpellTime) return false;        // 全局施法锁

            ClientMagic magic = user.GetMagic(Spell.Healing);
            if (magic == null) return false;                           // 没学治疗术

            if (CMain.Time <= magic.CastTime + magic.Delay) return false;   // 冷却中

            int cost = magic.Level * magic.LevelCost + magic.BaseCost;
            if (user.Stats[Stat.ManaPenaltyPercent] > 0)
                cost += (cost * user.Stats[Stat.ManaPenaltyPercent]) / 100;
            if (cost > user.MP) return false;                          // 蓝不够

            user.NextMagic = magic;
            user.NextMagicObject = user;                               // 治疗自己
            user.NextMagicLocation = user.CurrentLocation;
            user.NextMagicDirection = user.Direction;

            return true;
        }

        private static void TryAutoUse(UserObject user, Stat stat, long now)
        {
            int percent = stat == Stat.HP ? Settings.AutoPotHPPercent : Settings.AutoPotMPPercent;
            int itemIndex = stat == Stat.HP ? Settings.AutoPotHPItem : Settings.AutoPotMPItem;
            if (itemIndex <= 0 || percent <= 0) return;

            long last = stat == Stat.HP ? LastHPTime : LastMPTime;
            if (last != 0 && now - last < MinUseInterval) return;

            int max = user.Stats[stat];
            if (max <= 0) return;

            int cur = stat == Stat.HP ? user.HP : user.MP;
            if (cur * 100L >= (long)max * percent) return;      // 百分比未低于阈值

            UserItem potion = FindPotion(user, itemIndex);
            if (potion == null) return;

            Network.Enqueue(new C.UseItem { UniqueID = potion.UniqueID, Grid = MirGridType.Inventory });

            if (stat == Stat.HP) LastHPTime = now;
            else LastMPTime = now;
        }

        /// <summary>在背包（含快捷栏 0..BeltIdx-1）中按 ItemInfo.Index 找一瓶药</summary>
        public static UserItem FindPotion(UserObject user, int itemIndex)
        {
            if (user == null || itemIndex <= 0) return null;

            UserItem[] items = user.Inventory;
            if (items == null) return null;

            for (int i = 0; i < items.Length; i++)
            {
                UserItem item = items[i];
                if (item == null || item.Info == null) continue;
                if (item.Info.Index != itemIndex) continue;
                return item;
            }

            return null;
        }

        /// <summary>当前配置的药水是否还在背包里</summary>
        public static bool HasPotion(UserObject user, int itemIndex)
        {
            return FindPotion(user, itemIndex) != null;
        }

        /// <summary>
        /// 背包里所有可自动使用的药水（按 Info.Index 去重）。
        /// preferStat 指定时，把真正回复该属性的药水排在前面（选 HP 药时先列加 HP 的）。
        /// </summary>
        public static System.Collections.Generic.List<UserItem> GetUsablePotions(UserObject user, Stat? preferStat = null)
        {
            var matched = new System.Collections.Generic.List<UserItem>();
            var others = new System.Collections.Generic.List<UserItem>();

            if (user == null || user.Inventory == null)
                return matched;

            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < user.Inventory.Length; i++)
            {
                UserItem item = user.Inventory[i];
                if (item == null || item.Info == null) continue;
                if (item.Info.Type != ItemType.Potion) continue;
                // 不限 Shape：玩家显式选定后只喝这一种，列表里也允许出现 buff 药/神秘水
                if (!seen.Add(item.Info.Index)) continue;

                bool match = preferStat == null || item.Info.Stats[preferStat.Value] > 0;
                (match ? matched : others).Add(item);
            }

            matched.AddRange(others);
            return matched;
        }

        /// <summary>把配置的药水切到下一个（循环），返回新的 ItemInfo.Index（没有药水返回 0）</summary>
        public static int CyclePotion(UserObject user, int currentIndex, bool backward = false)
        {
            var list = GetUsablePotions(user);
            if (list.Count == 0) return 0;

            int idx = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Info.Index == currentIndex) { idx = i; break; }

            if (idx < 0) return list[0].Info.Index;

            if (backward)
                idx = (idx - 1 + list.Count) % list.Count;
            else
                idx = (idx + 1) % list.Count;

            return list[idx].Info.Index;
        }
    }
}
