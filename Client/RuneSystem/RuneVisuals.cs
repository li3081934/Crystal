using System;
using System.Collections.Generic;
using System.Drawing;
using Client.MirObjects;
using Client.MirScenes;
using S = ServerPackets;

namespace Client.RuneSystem
{
    /// <summary>
    /// 符文表现层：符文"本次生效"时给角色/技能上色，以及给召唤物换贴图。
    ///
    /// 服务端在符文掷骰成功时下发 S.RuneProc（带 ARGB 与持续毫秒），
    /// 客户端在这里记录"某个对象在一段时间内用什么颜色"，绘制时由 MapObject.ApplyDrawColour()
    /// 与技能特效的绘制分支读取 —— 没有符文生效时返回 Color.White，视觉效果与原来完全一致。
    ///
    /// 换皮走 S.PetSkin（带图库编号），由 MonsterObject.ApplyRuneSkin() 切换 BodyLibrary。
    /// </summary>
    public static class RuneVisuals
    {
        private struct Tint
        {
            public Color Colour;
            public bool Body;
            public long Expire;
        }

        private struct Skin
        {
            public ushort Index;
            public long Expire;
        }

        private static readonly Dictionary<uint, Tint> Tints = new Dictionary<uint, Tint>();
        private static readonly Dictionary<uint, Skin> PetSkins = new Dictionary<uint, Skin>();

        /// <summary>换皮记录的有效期：召唤物离开视野再回来时还能认出它，同时避免 ObjectID 复用后残留。</summary>
        private const long SkinLifetime = 5 * 60 * 1000;

        public static void OnRuneProc(S.RuneProc p)
        {
            if (p == null || p.TintArgb == 0 || p.TintMs <= 0) return;

            var colour = Color.FromArgb(p.TintArgb);

            //顺手清掉过期条目，避免长期积累
            if (Tints.Count > 32)
            {
                long now = CMain.Time;
                var expired = new List<uint>();
                foreach (var pair in Tints)
                    if (pair.Value.Expire <= now) expired.Add(pair.Key);
                foreach (var id in expired) Tints.Remove(id);
            }

            Tints[p.ObjectID] = new Tint
            {
                Colour = colour,
                Body = p.TintBody,
                Expire = CMain.Time + p.TintMs,
            };

            // 让地图重绘（有些对象是按纹理缓存绘制的）
            try { GameScene.Scene?.MapControl?.Redraw(); } catch { }
        }

        /// <summary>该对象当前是否处于符文着色中（没有则返回 Color.White）。</summary>
        /// <param name="body">true = 角色本体用色；false = 技能特效/刀光用色。</param>
        public static Color GetTint(MapObject ob, bool body)
        {
            if (ob == null || Tints.Count == 0) return Color.White;

            if (!Tints.TryGetValue(ob.ObjectID, out Tint tint))
                return Color.White;

            if (tint.Expire <= CMain.Time)
            {
                Tints.Remove(ob.ObjectID);
                return Color.White;
            }

            //符文声明"只染刀光"时，身体保持原色
            if (body && !tint.Body) return Color.White;

            return tint.Colour;
        }

        /// <summary>把基础绘制颜色与符文着色相乘（默认 Color.White 时原样返回）。</summary>
        /// <param name="body">true = 角色本体；false = 技能特效/刀光。</param>
        public static Color Apply(MapObject ob, Color baseColour, bool body)
        {
            Color tint = GetTint(ob, body);
            if (tint == Color.White) return baseColour;

            return Color.FromArgb(
                baseColour.A,
                baseColour.R * tint.R / 255,
                baseColour.G * tint.G / 255,
                baseColour.B * tint.B / 255);
        }

        /// <summary>收到换皮包：记录编号，并立刻让地图上已有的那个对象换图库。</summary>
        public static void OnPetSkin(S.PetSkin p)
        {
            if (p == null) return;

            if (p.Skin == 0) PetSkins.Remove(p.ObjectID);
            else
                PetSkins[p.ObjectID] = new Skin
                {
                    Index = p.Skin,
                    Expire = CMain.Time + SkinLifetime,
                };

            if (MapControl.Objects.TryGetValue(p.ObjectID, out var ob) && ob is MonsterObject mob)
            {
                mob.ApplyRuneSkin(p.Skin);
            }
        }

        /// <summary>取某个对象当前的换皮编号（0 = 原贴图）。对象刚进入视野时用来还原状态。</summary>
        public static ushort GetPetSkin(uint objectId)
        {
            if (PetSkins.Count == 0) return 0;
            if (!PetSkins.TryGetValue(objectId, out Skin skin)) return 0;

            if (skin.Expire <= CMain.Time)
            {
                PetSkins.Remove(objectId);
                return 0;
            }

            return skin.Index;
        }

        public static void Clear()
        {
            Tints.Clear();
            PetSkins.Clear();
        }
    }
}
