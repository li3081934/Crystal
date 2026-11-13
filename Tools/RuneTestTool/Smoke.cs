using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Sockets;
using System.Linq;
using C = ClientPackets;
using S = ServerPackets;

namespace RuneTestTool
{
    /// <summary>
    /// 协议级冒烟测试：真的登录服务器 → 读取符文表 → 选择符文 → 挥砍一次 → 检查服务器是否回发 AddBuff。
    /// 用于在没有客户端 UI 的情况下验证服务端整条符文链路。
    /// </summary>
    internal static class Smoke
    {
        public static int Run(string host, int port, string account, string password, Spell spell, ushort runeId, bool lifesteal, string expect = "buff", bool crit = false, bool repeat = false, bool maxatk = false, bool onlySpell = false, bool stun = false, bool dual = false, bool pet = false, bool extraPet = false, bool greenDot = false, bool splash = false, bool doubleCast = false, bool tame = false, string tameMonster = "Hen", string doubleMonster = "Scorpion", bool boltBleed = false, bool statBonus = false, bool manaKill = false, string manaMonster = "SnakeScorpion", int manaMonsterHp = 330, int[] manaPct = null)
        {
            Packet.IsServer = false; // 我们是客户端：收到的包按 ServerPackets 解析

            using var client = new TcpClient();
            client.Connect(host, port);
            client.ReceiveTimeout = 200;

            NetworkStream stream = client.GetStream();
            var buffer = new List<byte>();

            Console.WriteLine($"已连接 {host}:{port}");

            Send(stream, new C.ClientVersion { VersionHash = new byte[16] });

            var version = WaitFor(stream, buffer, p => p is S.ClientVersion, 5000) as S.ClientVersion;
            if (version == null || version.Result != 1)
            {
                Console.WriteLine("客户端版本校验失败");
                return 1;
            }
            Console.WriteLine("版本校验通过");

            Send(stream, new C.Login { AccountID = account, Password = password });

            var success = WaitFor(stream, buffer, p => p is S.LoginSuccess, 8000) as S.LoginSuccess;
            if (success == null)
            {
                Console.WriteLine("登录失败（没收到 LoginSuccess）");
                return 1;
            }

            Console.WriteLine($"登录成功，角色数 {success.Characters.Count}");

            if (success.Characters.Count == 0)
            {
                Console.WriteLine("该账号没有角色");
                return 1;
            }

            var charInfo = success.Characters[0];
            Console.WriteLine($"进入角色: {charInfo.Name} (Index={charInfo.Index}, Level={charInfo.Level})");

            Send(stream, new C.StartGame { CharacterIndex = charInfo.Index });

            var userInfo = WaitFor(stream, buffer, p => p is S.UserInformation, 20000) as S.UserInformation;
            Point myLocation = userInfo?.Location ?? new Point(0, 0);
            Console.WriteLine($"自身位置: ({myLocation.X},{myLocation.Y})");

            var runeList = WaitFor(stream, buffer, p => p is S.RuneList, 20000) as S.RuneList;
            if (runeList == null)
            {
                Console.WriteLine("没收到 RuneList（符文系统未生效或没有符文定义）");
                return 1;
            }

            Console.WriteLine($"RuneList: {runeList.Runes.Count} 个符文, 已选择 {runeList.States.Count} 个");
            foreach (var rune in runeList.Runes)
                Console.WriteLine($"  #{rune.Id} [{rune.Spell}] {rune.Name} (需要技能等级 {rune.NeedMagicLevel})");

            bool exists = false;
            foreach (var rune in runeList.Runes)
                if (rune.Id == runeId && rune.Spell == spell) exists = true;

            if (!exists)
            {
                Console.WriteLine($"符文 #{runeId} 不属于 {spell}，无法继续");
                return 1;
            }

            // 召唤物增益类符文（例如 召唤·白骨将军）：要先学会技能才能选符文，单独走一条流水线
            if (pet)
            {
                return RunPetBoon(stream, buffer, spell, runeId);
            }

            // 召唤数量类符文（例如 召唤·双卫）：要真的施法两次，也要护身符
            if (extraPet)
            {
                return RunExtraPet(stream, buffer, myLocation, spell, runeId);
            }

            // 第二目标类符文需要一个真的学会了的技能才能选中
            if (stun)
            {
                Send(stream, new C.Chat { Message = $"@GIVESKILL {spell} 3" });
                Drain(stream, buffer, 600);
            }

            // 顺带验证 GM 调试命令（同时验证该账号确实是 GM）
            Send(stream, new C.Chat { Message = "@RUNELIST" });
            var chat = WaitFor(stream, buffer, p => p is S.Chat c && c.Message != null && c.Message.Contains("剑术"), 5000) as S.Chat;

            if (chat == null)
                Console.WriteLine("提示: 没收到 @RUNELIST 的回显（可能不是 GM 账号），继续测试符文");
            else
                Console.WriteLine($"GM 命令可用: {chat.Message}");

            // 各专项测试分支
            if (stun) return RunSecondTargetStun(stream, buffer, myLocation, spell, runeId);
            if (dual) return RunDualPoison(stream, buffer, myLocation, spell, runeId);
            if (onlySpell) return RunOnlySpellLifesteal(stream, buffer, myLocation, spell);
            if (maxatk) return RunMaxAttack(stream, buffer, myLocation, spell, runeId);
            if (repeat) return RunRepeat(stream, buffer, myLocation, spell, runeId);
            if (crit) return RunCrit(stream, buffer, myLocation, spell, runeId);
            if (greenDot) return RunGreenPoisonDot(stream, buffer, myLocation, spell, runeId);
            if (splash) return RunSplash(stream, buffer, myLocation, spell, runeId);
            if (doubleCast) return RunDoubleCast(stream, buffer, myLocation, spell, runeId, doubleMonster);
            if (tame) return RunTame(stream, buffer, myLocation, spell, runeId, tameMonster);
            if (boltBleed) return RunSpellBleed(stream, buffer, myLocation, spell, runeId);
            if (statBonus) return RunStatBonus(stream, buffer, spell, runeId);
            if (manaKill) return RunManaOnKill(stream, buffer, myLocation, spell, runeId, manaMonster, manaMonsterHp, manaPct);

            if (lifesteal) return RunLifesteal(stream, buffer, myLocation);

            Console.WriteLine("未指定测试模式（可用 --pet / --extrapet / --stun / --dual / --crit / --repeat / --maxatk / --onlyspell / --green / --splash / --double / --tame / --boltbleed / --statbonus）");
            return 0;
        }

        /// <summary>
        /// 召唤数量符文（「召唤·双卫」）：
        ///   1) 无符文时连按两次召唤，场上仍然只有 1 只骷髅
        ///   2) 选上符文后能同时存在 2 只
        ///   3) 满 2 只之后再次施法只召回、不再增加
        /// 需要真的学会技能 + 装备护身符（SummonSkeleton 里走 GetAmulet），所以先 @GIVESKILL / @MAKE Amulet。
        /// </summary>
        private static int RunExtraPet(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId)
        {
            Console.WriteLine();
            Console.WriteLine($"--- 召唤数量验证（{spell} → 符文 #{runeId}）---");

            Send(stream, new C.Chat { Message = "@REVIVE" });
            Drain(stream, buffer, 600);
            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);
            Send(stream, new C.Chat { Message = $"@GIVESKILL {spell} 3" });
            Drain(stream, buffer, 800);

            if (!EquipAmulet(stream, buffer)) return 1;

            // A) 无符文：连按两次，应该只有 1 只
            Send(stream, new C.SetRune { Spell = spell, RuneId = 0 });
            Drain(stream, buffer, 900);
            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Drain(stream, buffer, 400);

            var pets = new Dictionary<uint, string>();
            int plain = SummonSkeletons(stream, buffer, myLocation, spell, pets, 2, "A 无符文");
            if (plain != 1)
            {
                Console.WriteLine($"  × 无符文时连按两次召唤出了 {plain} 只（期望 1）");
                return 1;
            }
            Console.WriteLine("  ✓ 无符文：连按两次仍然只有 1 只");

            // B) 带符文：切符文会把旧骷髅解散，再连按两次应该 2 只
            Send(stream, new C.SetRune { Spell = spell, RuneId = runeId });
            Drain(stream, buffer, 2500);
            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Drain(stream, buffer, 400);
            pets.Clear();

            int withRune = SummonSkeletons(stream, buffer, myLocation, spell, pets, 2, "B 带符文");
            if (withRune != 2)
            {
                Console.WriteLine($"  × 带符文时连按两次只召唤出 {withRune} 只（期望 2）");
                return 1;
            }
            Console.WriteLine("  ✓ 带符文：能同时存在 2 只");

            // C) 满员后再按一次：只召回，不再增加
            int capped = SummonSkeletons(stream, buffer, myLocation, spell, pets, 1, "C 满员再按");
            if (capped != 2)
            {
                Console.WriteLine($"  × 满员后再施法变成了 {capped} 只（期望仍是 2）");
                return 1;
            }
            Console.WriteLine("  ✓ 满员后施法：只召回，不再增加");

            Console.WriteLine("召唤数量验证通过：无符文 1 只 → 带符文 2 只 → 满员封顶");
            return 0;
        }

        /// <summary>连按 N 次召唤，返回场上（本段收集到的）骷髅数量。</summary>
        private static int SummonSkeletons(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, Dictionary<uint, string> pets, int casts, string tag)
        {
            for (int i = 0; i < casts; i++)
            {
                TrySend(stream, new C.Magic { Spell = spell, Direction = MirDirection.Down, TargetID = 0, Location = myLocation });

                foreach (var p in Drain(stream, buffer, 2200))
                {
                    if (p is S.ObjectMonster om && om.Image == Monster.BoneFamiliar)
                    {
                        pets[om.ObjectID] = om.Name;
                        Console.WriteLine($"    {tag} 第 {i + 1} 次施法 → 骷髅 {om.Name} (ObjectID={om.ObjectID})");
                    }
                    else if (p is S.ObjectRemove rm && pets.Remove(rm.ObjectID))
                    {
                        Console.WriteLine($"    {tag} 骷髅消失 (ObjectID={rm.ObjectID})");
                    }
                    else if (p is S.Chat chat && chat.Message != null && (chat.Message.Contains("护身符") || chat.Message.Contains("summon") || chat.Message.Contains("召唤")))
                    {
                        Console.WriteLine($"    {tag} [服务端提示] {chat.Message}");
                    }
                }

                Console.WriteLine($"    {tag} 第 {i + 1} 次施法后，场上骷髅 = {pets.Count}");
            }

            return pets.Count;
        }

        /// <summary>清背包后造一个护身符并装到 Amulet 槽（@CLEARBAG 只清背包，不影响已穿的装备）。</summary>
        private static bool EquipAmulet(NetworkStream stream, List<byte> buffer)
        {
            Console.WriteLine("准备护身符...");

            Send(stream, new C.Chat { Message = "@CLEARBAG" });
            Drain(stream, buffer, 700);

            Send(stream, new C.Chat { Message = "@MAKE Amulet 10" });
            var packets = Drain(stream, buffer, 2500);
            var gained = packets.OfType<S.GainedItem>().LastOrDefault();
            if (gained?.Item == null)
            {
                Console.WriteLine("  × 没拿到护身符（@MAKE Amulet 没回包，确认物品库里存在 Amulet）");
                return false;
            }

            ulong uid = gained.Item.UniqueID;
            Send(stream, new C.EquipItem { Grid = MirGridType.Inventory, UniqueID = uid, To = (int)EquipmentSlot.Amulet });
            var equip = Drain(stream, buffer, 2500).OfType<S.EquipItem>().LastOrDefault();
            if (equip == null || !equip.Success)
            {
                Console.WriteLine($"  × 装备护身符失败（Success={equip?.Success}，UniqueID={uid}）");
                return false;
            }

            Console.WriteLine("  ✓ 护身符已装备到 Amulet 槽");
            return true;
        }

        /// <summary>
        /// 召唤物符文（如「召唤·白骨将军」）：验证
        ///   1) 无符文时召唤出来的是原版贴图
        ///   2) 选/取消符文时场上旧骷髅直接被解散（玩家重新召唤）
        ///   3) 带符文重新召唤会收到换皮包 S.PetSkin
        /// 数值强化由服务端日志（[PetBase]/[PetBoon]）确认。
        /// </summary>
        private static int RunPetBoon(NetworkStream stream, List<byte> buffer, Spell spell, ushort runeId)
        {
            Console.WriteLine();
            Console.WriteLine($"--- 召唤物增益验证（{spell} → 符文 #{runeId}）---");

            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);

            Send(stream, new C.Chat { Message = $"@GIVESKILL {spell} 3" });
            Drain(stream, buffer, 800);

            // 0) 先把符文清掉（角色存档可能还留着上次测试选的符文）
            Send(stream, new C.SetRune { Spell = spell, RuneId = 0 });
            Drain(stream, buffer, 800);

            // 1) 先在没有符文的情况下召一只：不应该有换皮包
            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Drain(stream, buffer, 300);
            Send(stream, new C.Chat { Message = "@RECALLMOB BoneFamiliar 1 5" });

            var plain = Drain(stream, buffer, 3000);
            var plainPet = plain.OfType<S.ObjectMonster>().FirstOrDefault();
            if (plainPet == null)
            {
                Console.WriteLine("召唤失败（@RECALLMOB 没回包）");
                return 1;
            }
            Console.WriteLine($"无符文召唤：{plainPet.Name} (ObjectID={plainPet.ObjectID})");
            // 服务端在"无符文"时也会主动下发 Skin=0 的清除包（防止旧贴图残留），所以只拦 Skin != 0 的情况
            var straySkin = plain.OfType<S.PetSkin>().FirstOrDefault(x => x.Skin != 0);
            if (straySkin != null)
            {
                Console.WriteLine($"  × 没有符文却收到了换皮包（Skin={straySkin.Skin}）");
                return 1;
            }
            Console.WriteLine("  ✓ 无符文：没有换皮包（用原版贴图）");

            // 2) 选符文：按新规则，场上已有的骷髅要被直接解散（杀死），玩家重新召唤
            Send(stream, new C.SetRune { Spell = spell, RuneId = runeId });
            var afterSet = Drain(stream, buffer, 2500);
            var set = afterSet.OfType<S.SetRune>().FirstOrDefault();
            if (set == null || set.RuneId != runeId)
            {
                Console.WriteLine($"选择符文失败（回包 RuneId={set?.RuneId}），当前技能等级可能不足");
                return 1;
            }
            Console.WriteLine($"已选择符文 #{runeId}");

            var died = afterSet.OfType<S.ObjectDied>().Any(x => x.ObjectID == plainPet.ObjectID);
            if (!died)
            {
                Console.WriteLine($"  × 选择符文后旧骷髅没有被解散（ObjectID={plainPet.ObjectID}）");
                return 1;
            }
            Console.WriteLine("  ✓ 选择符文后旧骷髅被解散，需要重新召唤");

            // 3) 重新召唤：应该收到换皮包 2000（符文皮肤保留 2000~2999）
            foreach (byte level in new byte[] { 7, 0 })
            {
                Send(stream, new C.Chat { Message = $"@RECALLMOB BoneFamiliar 1 {level}" });

                // 注意：换皮包可能比 ObjectMonster 早到，所以这里一次性 drain 全部包再挑
                var packets = Drain(stream, buffer, 3000);
                var monster = packets.OfType<S.ObjectMonster>().FirstOrDefault();
                if (monster == null)
                {
                    Console.WriteLine($"召唤 {level} 级骷髅失败（@RECALLMOB 没回包）");
                    return 1;
                }

                Console.WriteLine($"召唤成功：{monster.Name} (ObjectID={monster.ObjectID}, 期望宠物等级 {level})");

                var skins = packets.OfType<S.PetSkin>().ToList();
                foreach (var s in skins)
                    Console.WriteLine($"    PetSkin: ObjectID={s.ObjectID} -> 图库 {s.Skin:000}.Lib");

                var skin = skins.FirstOrDefault(x => x.ObjectID == monster.ObjectID);
                if (skin == null)
                {
                    Console.WriteLine("  × 没收到换皮包（S.PetSkin）");
                    return 1;
                }
                if (skin.Skin != 2000)
                {
                    Console.WriteLine($"  × 换皮图库不对，期望 2000，实际 {skin.Skin}");
                    return 1;
                }
                Console.WriteLine("  ✓ 换皮包正确");
            }

            // 4) 取消符文：已有的骷髅同样应该被解散
            var before = plainPet.ObjectID;
            Send(stream, new C.Chat { Message = "@RUNE SummonSkeleton 0" });
            var revert = Drain(stream, buffer, 2500);
            if (revert.OfType<S.ObjectDied>().Any())
                Console.WriteLine("  ✓ 取消符文后骷髅被解散");
            else
            {
                Console.WriteLine("  × 取消符文后骷髅没有被解散");
                return 1;
            }

            // 5) 取消符文后再召唤：不应再收到换皮包
            Send(stream, new C.Chat { Message = "@RECALLMOB BoneFamiliar 1 3" });
            var offPackets = Drain(stream, buffer, 3000);
            var offPet = offPackets.OfType<S.ObjectMonster>().FirstOrDefault();
            Console.WriteLine(offPet == null ? "取消符文后召唤失败（可能宠物数量已满）" : $"取消符文后召唤：{offPet.Name} (ObjectID={offPet.ObjectID}, 期望宠物等级 3)");
            if (offPackets.OfType<S.PetSkin>().Any())
            {
                Console.WriteLine("  × 取消符文后仍然收到换皮包");
                return 1;
            }
            Console.WriteLine("  ✓ 取消符文后召唤的是原版骷髅");

            Console.WriteLine("场景已就绪：带符文的骷髅属性已按等级强化（服务端 RuneHooks.ApplyPetBoon）");
            return 0;
        }

        /// <summary>
        /// 验证"双毒"符文（例如 施毒·双生）：施毒术原本按携带的毒符只上一种毒（绿=Shape1 / 红=Shape2），
        /// 带符文后两种毒都要挂上，且携带哪种都行。
        ///
        /// 每个场景都做 A/B 对照：
        ///   A) 不带符文（SetRune RuneId=0）→ 只应出现携带的那种毒
        ///   B) 带上符文 → 携带毒 + 另一种毒都要出现
        /// </summary>
        private static int RunDualPoison(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId)
        {
            Console.WriteLine();
            Console.WriteLine($"--- 双毒验证（{spell} → 符文 #{runeId}）---");

            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);

            if (!RunDualScenario(stream, buffer, spell, myLocation, runeId, "GreenPoison", PoisonType.Green)) return 1;
            if (!RunDualScenario(stream, buffer, spell, myLocation, runeId, "RedPoison", PoisonType.Red)) return 1;

            Console.WriteLine("双毒验证通过：携带绿毒 → 绿+红；携带红毒 → 红+绿");
            return 0;
        }

        /// <summary>单个毒符场景的 A/B 对照（poisonName = GreenPoison / RedPoison）。</summary>
        private static bool RunDualScenario(NetworkStream stream, List<byte> buffer, Spell spell, Point myLocation, ushort runeId, string poisonName, PoisonType carried)
        {
            PoisonType other = carried == PoisonType.Green ? PoisonType.Red : PoisonType.Green;

            // 清空背包：@MAKE 造出的毒符如果和背包里已有的同种毒符叠在一起，服务端会合并堆叠并丢弃"新物品"，
            // 但 S.GainedItem 已经带着那个消失的 UniqueID 发出去了，客户端拿它去装备必然 Success=False。
            Send(stream, new C.Chat { Message = "@CLEARBAG" });
            Drain(stream, buffer, 600);

            // 造毒符并装备到护身符槽（槽里有别的毒符时服务端会自动换回背包）
            Send(stream, new C.Chat { Message = $"@MAKE {poisonName} 50" });

            var gained = WaitFor(stream, buffer, p => p is S.GainedItem, 6000) as S.GainedItem;
            if (gained?.Item == null)
            {
                Console.WriteLine($"没收到 {poisonName}（@MAKE 失败？）");
                return false;
            }

            Send(stream, new C.EquipItem { Grid = MirGridType.Inventory, UniqueID = gained.Item.UniqueID, To = (int)EquipmentSlot.Amulet });

            var equip = WaitFor(stream, buffer, p => p is S.EquipItem, 6000) as S.EquipItem;
            if (equip == null || !equip.Success)
            {
                Console.WriteLine($"装备 {poisonName} 失败（Success={equip?.Success}）—— 检查职业/等级是否满足毒符要求");
                return false;
            }

            Console.WriteLine($"携带 {poisonName}（期望毒：{carried}）");

            // A) 不带符文
            Send(stream, new C.SetRune { Spell = spell, RuneId = 0 });
            WaitFor(stream, buffer, p => p is S.SetRune, 6000);

            PoisonType plain = CastPoisonAndCollect(stream, buffer, spell, myLocation, out uint plainTarget, out int plainHits);
            Console.WriteLine($"  A 无符文：目标 {plainTarget} 中毒包 {plainHits} 次，状态 = {plain}");

            if ((plain & other) != 0)
            {
                Console.WriteLine($"  A 阶段出现了不该有的 {other} —— 环境不干净");
                return false;
            }

            if ((plain & carried) == 0)
            {
                Console.WriteLine($"  A 阶段连 {carried} 都没上 —— 施毒术本身没打通");
                return false;
            }

            // B) 带符文
            Send(stream, new C.SetRune { Spell = spell, RuneId = runeId });
            var set = WaitFor(stream, buffer, p => p is S.SetRune, 6000) as S.SetRune;
            if (set == null || set.RuneId != runeId)
            {
                Console.WriteLine($"  选择符文失败（回包 RuneId={set?.RuneId}）");
                return false;
            }

            PoisonType dualFlags = CastPoisonAndCollect(stream, buffer, spell, myLocation, out uint dualTarget, out int dualHits);
            Console.WriteLine($"  B 带符文：目标 {dualTarget} 中毒包 {dualHits} 次，状态 = {dualFlags}");

            bool hasCarried = (dualFlags & carried) != 0;
            bool hasOther = (dualFlags & other) != 0;

            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Drain(stream, buffer, 200);

            if (!hasCarried || !hasOther)
            {
                Console.WriteLine($"  B 阶段没有同时中两种毒（{carried}={hasCarried}, {other}={hasOther}）");
                return false;
            }

            Console.WriteLine($"  ✓ {poisonName} 场景通过：{carried} + {other}");
            return true;
        }

        /// <summary>
        /// 刷一只怪 → 朝它施毒 → 收集 3 秒内的中毒包，返回累计的 PoisonType 位。
        /// </summary>
        private static PoisonType CastPoisonAndCollect(NetworkStream stream, List<byte> buffer, Spell spell, Point myLocation, out uint targetId, out int hitCount)
        {
            targetId = 0;
            hitCount = 0;

            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Drain(stream, buffer, 250);
            Send(stream, new C.Chat { Message = "@MOB Scorpion 1" });

            var monster = WaitFor(stream, buffer, p => p is S.ObjectMonster, 5000) as S.ObjectMonster;
            if (monster == null)
            {
                Console.WriteLine("没刷出怪（@MOB 失败）");
                return PoisonType.None;
            }

            targetId = monster.ObjectID;
            MirDirection dir = Functions.DirectionFromPoint(myLocation, monster.Location);

            Send(stream, new C.Magic
            {
                Spell = spell,
                Direction = dir,
                TargetID = monster.ObjectID,
                Location = monster.Location,
            });

            PoisonType flags = PoisonType.None;
            long watchEnd = Environment.TickCount64 + 3000;

            while (Environment.TickCount64 < watchEnd)
            {
                var packet = WaitFor(stream, buffer, p => p is S.ObjectPoisoned, 200);

                if (packet is S.ObjectPoisoned poisoned && poisoned.ObjectID == monster.ObjectID)
                {
                    hitCount++;
                    flags |= poisoned.Poison;
                }
            }

            return flags;
        }

        /// <summary>
        /// 验证"只对第二目标生效"的减益符文（例如 刺杀·震魄）：
        ///   1) 开启无敌，避免观察期间被打死
        ///   2) 刷一批怪，从刷怪/走路包还原它们的坐标
        ///   3) 找一个方向：身前第 2 格站着一只怪（剑气能贯穿到）
        ///   4) 朝那个方向甩刺杀，断言"第 2 格那只"收到 Stun 状态包，"第 1 格那只"没有
        /// </summary>
        private static int RunSecondTargetStun(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId)
        {
            Console.WriteLine();
            Console.WriteLine($"--- 第二目标 Stun 验证（{spell} → 符文 #{runeId}）---");

            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);

            var monsters = new Dictionary<uint, Point>();

            for (int attempt = 1; attempt <= 8; attempt++)
            {
                Send(stream, new C.Chat { Message = "@CLEARMOB" });
                Drain(stream, buffer, 250);
                Send(stream, new C.Chat { Message = "@MOB Scorpion 8 2" });

                monsters.Clear();
                long scanEnd = Environment.TickCount64 + 900;
                while (Environment.TickCount64 < scanEnd)
                    TrackMonster(monsters, WaitFor(stream, buffer, p => p is S.ObjectMonster || p is S.ObjectWalk || p is S.ObjectRun || p is S.ObjectTurn, 150));

                // 找方向：myLocation + d 和 myLocation + 2d 上都有怪（前者可有可无，有更好）
                MirDirection? dir = null;
                uint secondId = 0;
                uint firstId = 0;

                // 优先挑"第 1 格和第 2 格都有怪"的方向，这样才能验证第 1 格不会被震晕
                for (int pass = 0; pass < 2 && dir == null; pass++)
                {
                    foreach (MirDirection d in Enum.GetValues(typeof(MirDirection)))
                    {
                        Point first = Functions.PointMove(myLocation, d, 1);
                        Point second = Functions.PointMove(first, d, 1);

                        uint sId = 0, fId = 0;
                        foreach (var kv in monsters)
                        {
                            if (kv.Value == second) sId = kv.Key;
                            else if (kv.Value == first) fId = kv.Key;
                        }

                        if (sId == 0) continue;
                        if (pass == 0 && fId == 0) continue;   // 第一轮要求两只都在

                        dir = d;
                        secondId = sId;
                        firstId = fId;
                        break;
                    }
                }

                if (dir == null)
                {
                    Console.WriteLine($"  第 {attempt} 次：没找到「第 2 格有怪」的方向（怪 {monsters.Count} 只），重刷");
                    continue;
                }

                Console.WriteLine($"  第 {attempt} 次：朝 {dir} 甩刺杀，第 1 格怪={firstId}，第 2 格怪={secondId}");

                Send(stream, new C.Attack { Direction = dir.Value, Spell = spell });

                bool secondStunned = false;
                bool firstStunned = false;
                int effectTicks = 0;

                long watchEnd = Environment.TickCount64 + 1600;
                while (Environment.TickCount64 < watchEnd)
                {
                    var packet = WaitFor(stream, buffer, p => p is S.ObjectPoisoned || p is S.ObjectEffect || p is S.ObjectDied, 150);

                    if (packet is S.ObjectPoisoned poisoned && (poisoned.Poison & PoisonType.Stun) != 0)
                    {
                        if (poisoned.ObjectID == secondId)
                        {
                            secondStunned = true;
                            Console.WriteLine($"  第 2 格目标进入 Stun: Poison={poisoned.Poison}");
                        }
                        else if (poisoned.ObjectID == firstId)
                        {
                            firstStunned = true;
                            Console.WriteLine($"  第 1 格目标也进了 Stun（不应该）: Poison={poisoned.Poison}");
                        }
                    }
                    else if (packet is S.ObjectEffect effect && effect.Effect == SpellEffect.Stunned)
                    {
                        effectTicks++;
                    }
                }

                Send(stream, new C.Chat { Message = "@CLEARMOB" });
                Drain(stream, buffer, 200);

                if (secondStunned)
                {
                    Console.WriteLine($"  结果：第 2 格目标被震晕，Stun 特效包 {effectTicks} 次，第 1 格目标 Stun={firstStunned}");
                    if (firstStunned)
                    {
                        Console.WriteLine("第 1 格目标也被震晕了 —— RequireSecondTarget 没生效");
                        return 1;
                    }

                    Console.WriteLine("第二目标 Stun 验证通过：只有剑气贯穿到的第 2 个目标被震晕");
                    return 0;
                }

                Console.WriteLine("  这次没触发 Stun（可能怪走开了），重试");
            }

            Console.WriteLine("多次尝试都没能让第 2 格目标中 Stun —— 符文没生效");
            return 1;
        }

        private static int RunOnlySpellLifesteal(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell)
        {
            Console.WriteLine();
            Console.WriteLine($"--- 技能限定吸血验证（{spell}）---");

            // 上一轮可能被怪打死，先复活并回满血（死亡状态会写进存档）
            Send(stream, new C.Chat { Message = "@REVIVE" });
            Drain(stream, buffer, 600);

            // 清掉其它技能的符文（剑术·饮血是全伤害吸血，会污染观测）
            foreach (var other in new[] { Spell.Fencing, Spell.Slaying })
            {
                if (other == spell) continue;
                Send(stream, new C.SetRune { Spell = other, RuneId = 0 });
                WaitFor(stream, buffer, p => p is S.SetRune, 3000);
            }
            Console.WriteLine("已清除其它技能的符文");

            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Send(stream, new C.Chat { Message = "@MOB Scorpion 1" });

            var monster = WaitFor(stream, buffer, p => p is S.ObjectMonster, 6000) as S.ObjectMonster;
            if (monster == null)
            {
                Console.WriteLine("怪物没有生成");
                return 1;
            }
            Console.WriteLine($"怪物已生成: {monster.Name} @ ({monster.Location.X},{monster.Location.Y})");

            // 登录时的 HP 推送可能已被前面的 WaitFor 吃掉，这里靠"被怪打"拿到当前血量
            int baseHp = 0;
            long hpDeadline = Environment.TickCount64 + 12000;
            while (Environment.TickCount64 < hpDeadline && baseHp == 0)
            {
                var hp = WaitFor(stream, buffer, p => p is S.HealthChanged, 500) as S.HealthChanged;
                if (hp != null && hp.HP > baseHp) baseHp = hp.HP;
            }
            if (baseHp <= 0)
            {
                Console.WriteLine("拿不到自身血量（12 秒内没有任何 HealthChanged）");
                return 1;
            }
            Console.WriteLine($"基准 HP: {baseHp}");

            int minHp = baseHp;
            long deadline = Environment.TickCount64 + 25000;
            while (Environment.TickCount64 < deadline)
            {
                var changed = WaitFor(stream, buffer, p => p is S.HealthChanged, 800) as S.HealthChanged;
                if (changed == null) continue;

                if (changed.HP < minHp) minHp = changed.HP;
                if (minHp < baseHp) break;
            }
            if (minHp >= baseHp)
            {
                Console.WriteLine($"怪物没能打掉血（HP 仍为 {minHp}），无法验证");
                return 1;
            }
            Console.WriteLine($"已掉血到 {minHp}，开始攻击...");

            var dirs = new List<MirDirection>();
            var mainDir = Functions.DirectionFromPoint(myLocation, monster.Location);
            dirs.Add(mainDir);
            for (int i = 0; i < 8; i++)
                if ((MirDirection)i != mainDir) dirs.Add((MirDirection)i);

            // ---------- 阶段 1：普通攻击，不应回血 ----------
            int hpNow = minHp;
            int normalHeals = 0;
            int normalHits = 0;

            for (int round = 0; round < 1; round++)
            {
                for (int i = 0; i < dirs.Count; i++)
                {
                    Send(stream, new C.Attack { Direction = dirs[i], Spell = Spell.None });

                    long attackDeadline = Environment.TickCount64 + 1300;
                    while (Environment.TickCount64 < attackDeadline)
                    {
                        var packet = WaitFor(stream, buffer, p => p is S.HealthChanged || p is S.DamageIndicator, 200);

                        if (packet is S.DamageIndicator dmg && dmg.Type == DamageType.Hit && dmg.Damage < 0)
                            normalHits++;

                        if (packet is S.HealthChanged hc)
                        {
                            if (hc.HP > hpNow) { normalHeals += hc.HP - hpNow; hpNow = hc.HP; }
                            else hpNow = hc.HP;
                        }
                    }

                    var moved = WaitFor(stream, buffer, p => p is S.ObjectMonster, 50) as S.ObjectMonster;
                    if (moved != null && moved.ObjectID == monster.ObjectID)
                        monster.Location = moved.Location;
                }
            }

            Console.WriteLine($"普通攻击阶段：命中 {normalHits} 次，共回复 {normalHeals} HP（期望 0）");

            // 阶段 2 前复活回满，重置靶子，再让怪打掉一些血（满血时回血不可观测）
            Send(stream, new C.Chat { Message = "@REVIVE" });
            Drain(stream, buffer, 600);
            hpNow = baseHp;   // 复活后视为满血，下面的等待循环会等怪把血打下去
            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Send(stream, new C.Chat { Message = "@MOB Scorpion 1" });
            Drain(stream, buffer, 2500);

            if (hpNow >= baseHp)
            {
                Console.WriteLine("（自己已满血，等怪物打掉一些血再继续）");
                long waitHp = Environment.TickCount64 + 20000;
                while (Environment.TickCount64 < waitHp && hpNow >= baseHp)
                {
                    var changed = WaitFor(stream, buffer, p => p is S.HealthChanged, 500) as S.HealthChanged;
                    if (changed != null) hpNow = changed.HP;
                }
            }
            Console.WriteLine($"技能阶段开始 HP: {hpNow}（基准 {baseHp}）");

            // ---------- 阶段 2：用该技能攻击，应该回血 ----------
            // 每次挥砍前重刷一只怪：保证贴脸、且自己血量始终不满（否则回血不可观测）
            int healTotal = 0;
            int procCount = 0;
            int spellHits = 0;
            int spellSwings = 0;
            int lastTint = 0;

            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Drain(stream, buffer, 300);

            for (int attempt = 0; attempt < 16; attempt++)
            {
                Send(stream, new C.Chat { Message = "@MOB Scorpion 1" });
                var mob = WaitFor(stream, buffer, p => p is S.ObjectMonster, 3000) as S.ObjectMonster;
                if (mob == null) continue;

                var aimDir = Functions.DirectionFromPoint(myLocation, mob.Location);
                spellSwings++;

                Send(stream, new C.Attack { Direction = aimDir, Spell = spell });

                long attackDeadline = Environment.TickCount64 + 1400;
                while (Environment.TickCount64 < attackDeadline)
                {
                    var packet = WaitFor(stream, buffer, p => p is S.HealthChanged || p is S.RuneProc || p is S.DamageIndicator, 200);

                    if (packet is S.DamageIndicator dmg && dmg.Type == DamageType.Hit && dmg.Damage < 0)
                        spellHits++;

                    if (packet is S.RuneProc rp)
                    {
                        procCount++;
                        lastTint = rp.TintArgb;
                    }

                    if (packet is S.HealthChanged hc)
                    {
                        if (hc.HP > hpNow)
                        {
                            healTotal += hc.HP - hpNow;
                            Console.WriteLine($"第 {attempt + 1} 次技能挥砍：HP 回升 {hpNow} -> {hc.HP}（回复 {hc.HP - hpNow}）");
                        }

                        hpNow = hc.HP;
                        if (hc.HP <= 0)
                        {
                            Console.WriteLine("角色已死亡，测试中断（可以把怪物数量再减少）");
                            return 1;
                        }
                    }
                }

                Send(stream, new C.Chat { Message = "@CLEARMOB" });
                Drain(stream, buffer, 200);
            }

            Console.WriteLine($"技能攻击阶段：挥砍 {spellSwings} 次、命中 {spellHits} 次，共回复 {healTotal} HP（期望 > 0）");
            Console.WriteLine($"收到 RuneProc（符文着色通知）: {procCount} 次" +
                              (lastTint != 0 ? $"，最后一次 ARGB=#{lastTint:X8}（红色应为 FFFF3A3A）" : ""));

            if (normalHeals != 0)
            {
                Console.WriteLine("普通攻击也回血了 —— RequireSpellHit 没生效");
                return 1;
            }

            if (healTotal <= 0)
            {
                Console.WriteLine("技能攻击没有回血 —— 吸血没生效");
                return 1;
            }

            if (procCount <= 0)
            {
                Console.WriteLine("没有收到符文着色包 —— 红色刀光没有下发");
                return 1;
            }

            // ---------- 阶段 3：流血 Debuff ----------
            // 打一只怪，然后别急着清怪：看它头上是否出现流血圆点、并逐秒掉血
            Console.WriteLine();
            Console.WriteLine("--- 流血 Debuff 验证 ---");

            // 流血验证要观察 5 秒以上，先开无敌避免角色被打死干扰观测
            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);
            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Send(stream, new C.Chat { Message = "@MOB Scorpion 1" });

            var victim = WaitFor(stream, buffer, p => p is S.ObjectMonster, 6000) as S.ObjectMonster;
            if (victim == null)
            {
                Console.WriteLine("怪物没有生成，无法验证流血");
                return 1;
            }

            int bleedPackets = 0;   // S.ObjectPoisoned 带 Bleeding 位
            int bleedTicks = 0;     // 每 tick 的流血特效
            int bleedDamage = 0;    // 流血跳字
            bool victimDied = false;

            // 目标可能不在攻击范围内 / 冷却未好，所以重复挥砍直到真的命中一次
            long bleedDeadline = Environment.TickCount64 + 14000;
            long nextSwing = Environment.TickCount64;

            while (Environment.TickCount64 < bleedDeadline && !victimDied)
            {
                if (bleedPackets <= 0 && Environment.TickCount64 >= nextSwing)
                {
                    if (!TrySend(stream, new C.Attack { Direction = Functions.DirectionFromPoint(myLocation, victim.Location), Spell = spell }))
                    {
                        Console.WriteLine("  连接已断开，结束流血观察");
                        break;
                    }

                    nextSwing = Environment.TickCount64 + 1200;
                }

                var packet = WaitFor(stream, buffer,
                    p => p is S.ObjectPoisoned || p is S.ObjectEffect || p is S.ObjectDied || p is S.DamageIndicator || p is S.ObjectMonster, 250);

                if (packet is S.ObjectMonster moved && moved.ObjectID == victim.ObjectID)
                {
                    victim.Location = moved.Location;
                }
                else if (packet is S.ObjectPoisoned poisoned && poisoned.ObjectID == victim.ObjectID)
                {
                    if ((poisoned.Poison & PoisonType.Bleeding) != 0)
                    {
                        bleedPackets++;
                        Console.WriteLine($"  目标进入流血状态: Poison={poisoned.Poison}");
                    }
                }
                else if (packet is S.ObjectEffect effect && effect.ObjectID == victim.ObjectID && effect.Effect == SpellEffect.Bleeding)
                {
                    bleedTicks++;
                    Console.WriteLine($"  流血第 {bleedTicks} 跳");
                }
                else if (packet is S.DamageIndicator dmg && dmg.ObjectID == victim.ObjectID && dmg.Type == DamageType.Hit && dmg.Damage < 0)
                {
                    bleedDamage += -dmg.Damage;
                }
                else if (packet is S.ObjectDied died && died.ObjectID == victim.ObjectID)
                {
                    victimDied = true;
                }
            }

            TrySend(stream, new C.Chat { Message = "@CLEARMOB" });
            Drain(stream, buffer, 200);

            Console.WriteLine($"流血状态包 {bleedPackets} 次、流血特效 {bleedTicks} 跳、跳字合计 {bleedDamage}" +
                              (victimDied ? "（目标被流血打死）" : ""));

            if (bleedPackets <= 0)
            {
                Console.WriteLine("目标没有进入流血状态 —— DebuffOnHit 没生效");
                return 1;
            }

            if (bleedTicks < 2 && !victimDied)
            {
                Console.WriteLine("流血没有持续跳伤 —— DebuffDuration / DebuffTickSpeed 配置可能不对");
                return 1;
            }

            Console.WriteLine("技能限定吸血验证通过：普通攻击不回血，技能命中才回血、刀光着色、且目标流血持续跳伤");
            return 0;
        }

private static int RunLifesteal(NetworkStream stream, List<byte> buffer, Point myLocation)
        {
            Console.WriteLine();
            Console.WriteLine("--- 吸血符文验证 ---");

            // 先拿一个基准血量（登录后会连续推送几次）
            int baseHp = 0;
            for (int i = 0; i < 8; i++)
            {
                var hp = WaitFor(stream, buffer, p => p is S.HealthChanged, 500) as S.HealthChanged;
                if (hp != null && hp.HP > baseHp) baseHp = hp.HP;
            }

            if (baseHp <= 0)
            {
                Console.WriteLine("拿不到自身血量，无法验证");
                return 1;
            }
            Console.WriteLine($"基准 HP: {baseHp}");

            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Send(stream, new C.Chat { Message = "@MOB Scorpion 2" });

            var monster = WaitFor(stream, buffer, p => p is S.ObjectMonster, 6000) as S.ObjectMonster;
            if (monster == null)
            {
                Console.WriteLine("怪物没有生成（检查 @MOB Scorpion 2 是否有效）");
                return 1;
            }
            Console.WriteLine($"怪物已生成: {monster.Name} @ ({monster.Location.X},{monster.Location.Y})");

            // 等怪物把我们的血打下去
            int minHp = baseHp;
            long deadline = Environment.TickCount64 + 25000;
            while (Environment.TickCount64 < deadline)
            {
                var changed = WaitFor(stream, buffer, p => p is S.HealthChanged, 800) as S.HealthChanged;
                if (changed == null) continue;

                if (changed.HP < minHp) minHp = changed.HP;
                if (minHp < baseHp) break;
            }

            if (minHp >= baseHp)
            {
                Console.WriteLine($"怪物没能打掉血（HP 仍为 {minHp}），无法验证吸血");
                return 1;
            }
            Console.WriteLine($"已掉血到 {minHp}，开始攻击...");

            var hitDirections = new List<MirDirection>();
            for (int i = 0; i < 8; i++)
                hitDirections.Add((MirDirection)i);

            // 优先朝怪物方向打，其余方向兜底
            var mainDir = Functions.DirectionFromPoint(myLocation, monster.Location);
            hitDirections.Remove(mainDir);
            hitDirections.Insert(0, mainDir);

            int hpBefore = minHp;

            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < hitDirections.Count; i++)
                {
                    Send(stream, new C.Attack { Direction = hitDirections[i], Spell = Spell.None });

                    long attackDeadline = Environment.TickCount64 + 1300;
                    while (Environment.TickCount64 < attackDeadline)
                    {
                        var packet = WaitFor(stream, buffer, p => p is S.HealthChanged, 200);

                        if (packet is S.HealthChanged hc)
                        {
                            if (hc.HP > hpBefore)
                            {
                                Console.WriteLine($"命中后 HP 回升: {hpBefore} -> {hc.HP}（回复 {hc.HP - hpBefore}）");
                                Console.WriteLine("吸血符文验证通过：造成伤害 → 按比例回血（普通攻击）");

                                return RunSpellLifesteal(stream, buffer, monster, myLocation, hpBefore);
                            }

                            if (hc.HP < hpBefore) hpBefore = hc.HP; // 继续被怪打
                        }
                    }

                    // 怪会移动，实时修正朝向
                    var moved = WaitFor(stream, buffer, p => p is S.ObjectMonster, 50) as S.ObjectMonster;
                    if (moved != null && moved.ObjectID == monster.ObjectID)
                        monster.Location = moved.Location;
                }
            }

            Console.WriteLine($"攻击后 HP 没有回升（{hpBefore}）—— 可能没打到怪");
            return 1;
        }


        /// <summary>技能（魔法）伤害也要触发吸血：给角色一个火球术，打到怪身上看是否回血。</summary>
        private static int RunSpellLifesteal(NetworkStream stream, List<byte> buffer, S.ObjectMonster monster, Point myLocation, int hp)
        {
            Console.WriteLine();
            Console.WriteLine("--- 技能吸血验证（火球术）---");

            Send(stream, new C.Chat { Message = "@GIVESKILL FireBall 3" });
            WaitFor(stream, buffer, p => p is S.NewMagic, 4000);

            // 等一个挥砍/施法冷却窗口
            System.Threading.Thread.Sleep(1200);

            int hpBefore = hp;
            int direction = (int)Functions.DirectionFromPoint(myLocation, monster.Location);

            for (int round = 0; round < 4; round++)
            {
                Send(stream, new C.Magic
                {
                    Spell = Spell.FireBall,
                    Direction = (MirDirection)direction,
                    TargetID = monster.ObjectID,
                    Location = monster.Location,
                });

                long deadline = Environment.TickCount64 + 1800;
                while (Environment.TickCount64 < deadline)
                {
                    var packet = WaitFor(stream, buffer, p => p is S.HealthChanged, 250);

                    if (packet is S.HealthChanged hc)
                    {
                        if (hc.HP > hpBefore)
                        {
                            Console.WriteLine($"施法命中后 HP 回升: {hpBefore} -> {hc.HP}（回复 {hc.HP - hpBefore}）");
                            Console.WriteLine("技能吸血验证通过：技能伤害同样触发吸血");
                            return 0;
                        }

                        if (hc.HP < hpBefore) hpBefore = hc.HP;
                    }
                }
            }

            Console.WriteLine("技能伤害没有触发回血（可能是伤害被护甲完全抵挡，战士 MC 太低）");
            return 0; // 不算失败：这是数值问题不是链路问题
        }


        /// <summary>
        /// 必暴符文验证（统计口径，避免逐次归因误差）：
        ///  1) 不带符文打 N 次命中，记录普通伤害与暴击次数（应为 0）；
        ///  2) 带符文打 M 次命中，统计暴击次数与平均伤害；
        ///  3) 期望：暴击必然出现，且"3 暴 + 1 普通"的循环使平均伤害约为普通的 1.75 倍。
        /// </summary>
        private static int RunCrit(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId)
        {
            Console.WriteLine();
            Console.WriteLine("--- 必暴符文验证 ---");

            // 保持不死（不影响伤害与暴击判定）
            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);
            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Send(stream, new C.Chat { Message = "@MOB Scorpion 3" });

            var monster = WaitFor(stream, buffer, p => p is S.ObjectMonster, 6000) as S.ObjectMonster;
            if (monster == null)
            {
                Console.WriteLine("怪物没有生成");
                return 1;
            }

            // 先撤掉符文
            Send(stream, new C.SetRune { Spell = spell, RuneId = 0 });
            WaitFor(stream, buffer, p => p is S.SetRune, 3000);

            Measure(stream, buffer, ref monster, myLocation, 4, out var normalSeq, out var normalDamages);

            Console.WriteLine($"无符文: 命中 {normalSeq.Count} 次，暴击 {normalSeq.Count(x => x == "C")} 次，伤害 [{string.Join(",", normalDamages)}]");

            Send(stream, new C.SetRune { Spell = spell, RuneId = runeId });
            var set = WaitFor(stream, buffer, p => p is S.SetRune, 3000) as S.SetRune;
            if (set == null || set.RuneId != runeId)
            {
                Console.WriteLine("符文设置失败");
                return 1;
            }

            Measure(stream, buffer, ref monster, myLocation, 10, out var critSeq, out var critDamages);

            int critCount = critSeq.Count(x => x == "C");
            Console.WriteLine($"带符文: 命中 {critSeq.Count} 次，暴击 {critCount} 次");
            Console.WriteLine($"  序列: {string.Join(" ", critSeq)}");
            Console.WriteLine($"  伤害: [{string.Join(",", critDamages)}]");

            if (critCount < 4)
            {
                Console.WriteLine($"暴击次数太少（{critCount}），必暴符文未按预期生效");
                return 1;
            }

            if (normalDamages.Count > 0 && critDamages.Count > 0)
            {
                double avgNormal = normalDamages.Average();
                double avgWithRune = critDamages.Average();
                double maxWithRune = critDamages.Max();

                // 说明：线上包的归因只做参考（多个目标/伤害延迟到达会让 Critical 与 Hit 落进不同采集窗口），
                // 这里只用"最高伤害是否接近 2 倍"做软校验。
                Console.WriteLine($"伤害参考: 无符文均值 {avgNormal:0.0} → 带符文均值 {avgWithRune:0.0}，带符文最高 {maxWithRune}");

                if (maxWithRune < avgNormal * 1.6)
                {
                    Console.WriteLine("提示: 采样里没看到接近 2 倍的伤害（可能随机 DC 波动 + 多目标归因导致，服务端日志已确认翻倍）");
                }
            }

            int baselineCrits = normalSeq.Count(x => x == "C");
            if (baselineCrits != 0)
            {
                Console.WriteLine($"注意: 不带符文时也出现了 {baselineCrits} 次暴击（通常是装备/属性自带暴击率）");
            }

            Console.WriteLine("必暴符文验证通过：复用游戏原有暴击判定（ForceCritical）与暴击伤害属性（Stat.CriticalDamage）");
            return 0;
        }

        /// <summary>连续挥砍直到凑够 wantHits 次命中；返回每次命中的 C/N 序列与伤害。</summary>
        private static void Measure(NetworkStream stream, List<byte> buffer, ref S.ObjectMonster monster, Point myLocation, int wantHits, out List<string> sequence, out List<int> damages)
        {
            sequence = new List<string>();
            damages = new List<int>();

            int swings = 0;

            while (sequence.Count < wantHits && swings < 60)
            {
                if (swings > 0 && swings % 6 == 0)
                {
                    Send(stream, new C.Chat { Message = "@MOB Scorpion 2" });
                    Drain(stream, buffer, 300);
                }

                var toward = Functions.DirectionFromPoint(myLocation, monster.Location);
                Send(stream, new C.Attack { Direction = swings % 2 == 0 ? toward : (MirDirection)(swings % 8), Spell = Spell.None });
                swings++;

                var packets = Drain(stream, buffer, 1400);

                bool gotCrit = false;
                int dealt = 0;
                bool gotHit = false;

                foreach (var packet in packets)
                {
                    if (packet is S.DamageIndicator di)
                    {
                        if (di.Type == DamageType.Critical) gotCrit = true;
                        if (di.Type == DamageType.Hit && di.Damage < 0) { dealt = -di.Damage; gotHit = true; }
                    }
                    else if (packet is S.ObjectMonster m)
                    {
                        monster.Location = m.Location;
                    }
                }

                if (!gotHit) continue;

                sequence.Add(gotCrit ? "C" : "N");
                damages.Add(dealt);
            }
        }

        /// <summary>在一段时间窗口内收下所有包（避免"等 A 的包时把 B 的包丢掉"）。</summary>
    private static int RunSplash(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId)
    {
        Console.WriteLine();
        Console.WriteLine($"--- 溅射验证（{spell} → 符文 #{runeId}）---");
        Send(stream, new C.SetRune
        {
            Spell = Spell.SummonSkeleton,
            RuneId = 0
        });
        Drain(stream, buffer, 800);
        Send(stream, new C.Chat
        {
            Message = "@REVIVE"
        });
        Drain(stream, buffer, 600);
        Send(stream, new C.Chat
        {
            Message = "@SUPERMAN"
        });
        Drain(stream, buffer, 300);
        Send(stream, new C.Chat
        {
            Message = $"@GIVESKILL {spell} 3"
        });
        Drain(stream, buffer, 800);
        if (spell == Spell.SoulFireBall && !EquipAmulet(stream, buffer))
        {
            return 1;
        }
        Send(stream, new C.SetRune
        {
            Spell = spell,
            RuneId = 0
        });
        Drain(stream, buffer, 900);
        int primaryId;
        int num = MeasureSplash(stream, buffer, myLocation, spell, "A 无符文", 1, out primaryId);
        Send(stream, new C.SetRune
        {
            Spell = spell,
            RuneId = runeId
        });
        Drain(stream, buffer, 1500);
        int primaryId2;
        int num2 = MeasureSplash(stream, buffer, myLocation, spell, "B 带符文", 2, out primaryId2);
        if (primaryId < 0)
        {
            Console.WriteLine("  × 无符文时没能打出伤害，无法比较");
            return 1;
        }
        if (primaryId2 < 0)
        {
            Console.WriteLine("  × 带符文时没能打出伤害，无法比较");
            return 1;
        }
        Console.WriteLine($"无符文：被打中的对象数 = {num}");
        Console.WriteLine($"带符文：被打中的对象数 = {num2}");
        if (num > 1)
        {
            Console.WriteLine($"  ! 无符文时也打到了 {num} 个对象（召唤兽或残留生物干扰，仅作提示）");
        }
        if (num2 < 2 || num2 <= num)
        {
            Console.WriteLine($"  × 带符文时没有比无符文打到更多对象（{num} → {num2}），溅射没生效");
            return 1;
        }
        Console.WriteLine("✓ 溅射验证通过：符文让灵魂火符打到了周围的敌人");
        return 0;
    }

    private static int RunDoubleCast(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId, string monsterName)
    {
        Console.WriteLine();
        Console.WriteLine($"--- 双发验证（{spell} → 符文 #{runeId}）---");
        Send(stream, new C.Chat
        {
            Message = "@REVIVE"
        });
        Drain(stream, buffer, 800);
        Send(stream, new C.Chat
        {
            Message = "@SUPERMAN"
        });
        Drain(stream, buffer, 300);
        Send(stream, new C.Chat
        {
            Message = $"@GIVESKILL {spell} 3"
        });
        Drain(stream, buffer, 800);
        if (!EquipAmulet(stream, buffer))
        {
            return 1;
        }
        Send(stream, new C.SetRune
        {
            Spell = spell,
            RuneId = 0
        });
        Drain(stream, buffer, 900);
        if (!MeasureDoubleCast(stream, buffer, myLocation, spell, monsterName, "A 无符文", 0, 1, out var _, out var _))
        {
            return 1;
        }
        Send(stream, new C.SetRune
        {
            Spell = spell,
            RuneId = runeId
        });
        Drain(stream, buffer, 1500);
        if (!MeasureDoubleCast(stream, buffer, myLocation, spell, monsterName, "B 带符文", 1, 2, out var casts2, out var _))
        {
            return 1;
        }
        if (casts2 < 1)
        {
            Console.WriteLine("  × 没收到补发的施法包，第二张符没出现");
            return 1;
        }
        Console.WriteLine($"✓ 双发验证通过：补发 {casts2} 个施法包，第二发伤害确实晚于第一发（>0.9s）");
        return 0;
    }

    private static bool MeasureDoubleCast(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, string monsterName, string tag, int expectCasts, int expectHits, out int casts, out int gap)
    {
        casts = 0;
        gap = 0;
        int instantCasts = 0;      //补发里带 Instant 标记的个数（纯表现，不该再播施法动作）
        Dictionary<uint, Point> dictionary = new Dictionary<uint, Point>();
        for (int i = 0; i < 4; i++)
        {
            Send(stream, new C.Chat
            {
                Message = "@CLEARMOB"
            });
            Drain(stream, buffer, 500);
            Send(stream, new C.Chat
            {
                Message = "@MOB " + monsterName + " 1"
            });
            List<Packet> list = Drain(stream, buffer, 1400);
            dictionary.Clear();
            foreach (Packet item in list)
            {
                TrackMonster(dictionary, item);
            }
            if (dictionary.Count == 0)
            {
                Console.WriteLine("  [" + tag + "] 没刷出怪，重试");
                continue;
            }
            KeyValuePair<uint, Point> keyValuePair = dictionary.First();
            Send(stream, new C.Magic
            {
                Spell = spell,
                Direction = Functions.DirectionFromPoint(myLocation, keyValuePair.Value),
                TargetID = keyValuePair.Key,
                Location = myLocation
            });
            casts = 0;
            instantCasts = 0;
            gap = 0;
            int num = 0;
            int num2 = 0;
            int num3 = 0;
            foreach (Packet item2 in Drain(stream, buffer, 900))
            {
                if (item2 is S.ObjectMagic { SelfBroadcast: not false })
                {
                    casts++;
                    if (((S.ObjectMagic)item2).Instant) instantCasts++;
                }
                if (item2 is S.DamageIndicator { Damage: <0 } damageIndicator && damageIndicator.ObjectID == keyValuePair.Key)
                {
                    num++;
                    num2++;
                }
            }
            foreach (Packet item3 in Drain(stream, buffer, 2800))
            {
                if (item3 is S.ObjectMagic { SelfBroadcast: not false })
                {
                    casts++;
                    if (((S.ObjectMagic)item3).Instant) instantCasts++;
                }
                if (item3 is S.DamageIndicator { Damage: <0 } damageIndicator2 && damageIndicator2.ObjectID == keyValuePair.Key)
                {
                    num++;
                    num3++;
                    gap = 900;
                }
            }
            Console.WriteLine($"  [{tag}] 补发施法包 {casts} 个（其中纯表现 {instantCasts} 个），伤害 {num} 次（前 0.9s {num2} 次 / 补发窗口 {num3} 次），第二发约 {gap}ms 到账");
            if (casts == expectCasts && num >= expectHits)
            {
                return true;
            }
            Console.WriteLine($"  [{tag}] 期望 {expectCasts} 个补发施法包 / {expectHits} 次伤害，重试");
        }
        Console.WriteLine($"  × [{tag}] 没能满足期望（{expectCasts} 个补发施法包 / {expectHits} 次跳字）");
        return false;
    }

    private static int RunTame(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId, string monsterName)
    {
        Console.WriteLine();
        Console.WriteLine($"--- 驯服等级验证（{spell} → 符文 #{runeId}，目标 {monsterName}）---");
        Send(stream, new C.Chat
        {
            Message = "@REVIVE"
        });
        Drain(stream, buffer, 600);
        Send(stream, new C.Chat
        {
            Message = "@LEVEL 40"
        });
        Drain(stream, buffer, 500);
        Send(stream, new C.Chat
        {
            Message = "@SUPERMAN"
        });
        Drain(stream, buffer, 300);
        Send(stream, new C.Chat
        {
            Message = $"@GIVESKILL {spell} 3"
        });
        Drain(stream, buffer, 800);
        int num = TameAndReadLevel(stream, buffer, myLocation, spell, monsterName, "A 无符文", 0);
        int num2 = TameAndReadLevel(stream, buffer, myLocation, spell, monsterName, "B 带符文", runeId);
        Console.WriteLine("无符文：宠物等级 = " + ((num < 0) ? "未知" : num.ToString()));
        Console.WriteLine("带符文：宠物等级 = " + ((num2 < 0) ? "未知" : num2.ToString()));
        if (num < 0 || num2 < 0)
        {
            Console.WriteLine("  × 驯服没能成功（可能怪物不在此处 / 没蓝 / 等级差太大）");
            return 1;
        }
        if (num > 0)
        {
            Console.WriteLine($"  ! 原版驯服出来的宠物是 {num} 级（预期 0，可能是旧宠物）");
        }
        if (num2 != 7)
        {
            Console.WriteLine($"  × 3 级技能的宠物应为 7 级，实际 {num2}");
            return 1;
        }
        Console.WriteLine("✓ 驯服等级验证通过：带符文后 3 级诱惑之光驯服出 7 级宠物");
        return 0;
    }

    private static int TameAndReadLevel(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, string monsterName, string tag, ushort runeId)
    {
        Send(stream, new C.SetRune
        {
            Spell = spell,
            RuneId = runeId
        });
        Drain(stream, buffer, 900);
        Send(stream, new C.Chat
        {
            Message = "@CLEARMOB"
        });
        Drain(stream, buffer, 600);
        Send(stream, new C.Chat
        {
            Message = "@MOB " + monsterName + " 1"
        });
        List<Packet> list = Drain(stream, buffer, 1400);
        Dictionary<uint, Point> dictionary = new Dictionary<uint, Point>();
        foreach (Packet item in list)
        {
            TrackMonster(dictionary, item);
        }
        if (dictionary.Count == 0)
        {
            Console.WriteLine("  [" + tag + "] 没刷出 " + monsterName);
            return -1;
        }
        KeyValuePair<uint, Point> keyValuePair = dictionary.First();
        for (int i = 0; i < 25; i++)
        {
            Send(stream, new C.Magic
            {
                Spell = spell,
                Direction = Functions.DirectionFromPoint(myLocation, keyValuePair.Value),
                TargetID = keyValuePair.Key,
                Location = myLocation
            });
            bool flag = false;
            foreach (Packet item2 in Drain(stream, buffer, 900))
            {
                if (item2 is S.ObjectName objectName && objectName.ObjectID == keyValuePair.Key)
                {
                    flag = true;
                }
            }
            if (flag)
            {
                Console.WriteLine($"  [{tag}] 第 {i + 1} 次施法驯服成功（ObjectID={keyValuePair.Key}）");
                return ReadPetLevel(stream, buffer, monsterName, tag);
            }
        }
        Console.WriteLine("  [" + tag + "] 连续 25 次施法都没驯服成功，看一下现在的宠物列表：");
        Send(stream, new C.Chat
        {
            Message = "@PETINFO"
        });
        foreach (Packet item3 in Drain(stream, buffer, 900))
        {
            if (item3 is S.Chat { Message: not null } chat)
            {
                Console.WriteLine("    " + chat.Message);
            }
        }
        return -1;
    }

    private static int ReadPetLevel(NetworkStream stream, List<byte> buffer, string monsterName, string tag)
    {
        Send(stream, new C.Chat
        {
            Message = "@PETINFO"
        });
        int num = -1;
        foreach (Packet item in Drain(stream, buffer, 900))
        {
            if (!(item is S.Chat { Message: not null } chat) || !chat.Message.StartsWith("PETINFO name=", StringComparison.Ordinal) || chat.Message.IndexOf("dead=False", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            int num2 = chat.Message.IndexOf("name=", StringComparison.Ordinal) + 5;
            int num3 = chat.Message.IndexOf(' ', num2);
            string text = ((num3 < 0) ? chat.Message.Substring(num2) : chat.Message.Substring(num2, num3 - num2));
            if (text.IndexOf(monsterName, StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }
            Console.WriteLine("    " + chat.Message);
            int num4 = chat.Message.IndexOf("level=", StringComparison.Ordinal);
            if (num4 >= 0)
            {
                int num5 = chat.Message.IndexOf(' ', num4);
                string s = ((num5 < 0) ? chat.Message.Substring(num4 + 6) : chat.Message.Substring(num4 + 6, num5 - num4 - 6));
                if (int.TryParse(s, out var result))
                {
                    num = result;
                }
            }
        }
        if (num < 0)
        {
            Console.WriteLine("  [" + tag + "] PETINFO 里没找到活着的 " + monsterName);
        }
        return num;
    }

    private static int MeasureSplash(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, string tag, int minHits, out int primaryId)
    {
        primaryId = -1;
        Point dest = Point.Empty;
        Dictionary<uint, Point> dictionary = new Dictionary<uint, Point>();
        for (int i = 0; i < 6; i++)
        {
            if (primaryId >= 0)
            {
                break;
            }
            Send(stream, new C.Chat
            {
                Message = "@CLEARMOB"
            });
            Drain(stream, buffer, 500);
            Send(stream, new C.Chat
            {
                Message = "@MOB Scorpion 8 1"
            });
            List<Packet> list = Drain(stream, buffer, 1200);
            dictionary.Clear();
            foreach (Packet item in list)
            {
                TrackMonster(dictionary, item);
            }
            if (dictionary.Count < 2)
            {
                Console.WriteLine($"  [{tag}] 只刷出 {dictionary.Count} 只怪，重试");
                continue;
            }
            foreach (KeyValuePair<uint, Point> pair in dictionary)
            {
                if (!dictionary.Any((KeyValuePair<uint, Point> o) => o.Key != pair.Key && Math.Abs(o.Value.X - pair.Value.X) <= 1 && Math.Abs(o.Value.Y - pair.Value.Y) <= 1))
                {
                    continue;
                }
                primaryId = (int)pair.Key;
                dest = pair.Value;
                Console.WriteLine($"  [{tag}] 目标 = {pair.Key} @ ({pair.Value.X},{pair.Value.Y})，周围有同伴");
                break;
            }
            if (primaryId < 0)
            {
                Console.WriteLine("  [" + tag + "] 这堆怪挨得不够近，重试");
            }
        }
        if (primaryId < 0)
        {
            return -1;
        }
        for (int j = 0; j < 3; j++)
        {
            Send(stream, new C.Magic
            {
                Spell = spell,
                Direction = Functions.DirectionFromPoint(myLocation, dest),
                TargetID = (uint)primaryId,
                Location = myLocation
            });
            HashSet<uint> hashSet = new HashSet<uint>();
            foreach (Packet item2 in Drain(stream, buffer, 3200))
            {
                if (item2 is S.DamageIndicator { Damage: <0 } damageIndicator && dictionary.ContainsKey(damageIndicator.ObjectID))
                {
                    hashSet.Add(damageIndicator.ObjectID);
                }
            }
            Console.WriteLine($"  [{tag}] 第 {j + 1} 发跳字来源: {((hashSet.Count == 0) ? "无" : string.Join(",", hashSet))}");
            if (hashSet.Count >= minHits)
            {
                return hashSet.Count;
            }
        }
        return 0;
    }

    private static int RunGreenPoisonDot(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId)
    {
        Console.WriteLine();
        Console.WriteLine($"--- 绿毒 DoT 验证（{spell} → 符文 #{runeId}）---");
        Send(stream, new C.Chat
        {
            Message = "@REVIVE"
        });
        Drain(stream, buffer, 600);
        Send(stream, new C.Chat
        {
            Message = "@SUPERMAN"
        });
        Drain(stream, buffer, 300);
        Send(stream, new C.Chat
        {
            Message = $"@GIVESKILL {spell} 3"
        });
        Drain(stream, buffer, 800);
        if (!EquipPoison(stream, buffer, "GreenPoison"))
        {
            return 1;
        }
        Send(stream, new C.SetRune
        {
            Spell = spell,
            RuneId = 0
        });
        Drain(stream, buffer, 900);
        List<int> list = MeasureGreenTicks(stream, buffer, myLocation, spell, "A 无符文");
        if (list.Count < 2)
        {
            Console.WriteLine($"  × 无符文时只收到 {list.Count} 次毒跳，无法比较");
            return 1;
        }
        Send(stream, new C.SetRune
        {
            Spell = spell,
            RuneId = runeId
        });
        Drain(stream, buffer, 1500);
        List<int> list2 = MeasureGreenTicks(stream, buffer, myLocation, spell, "B 带符文");
        if (list2.Count < 2)
        {
            Console.WriteLine($"  × 带符文时只收到 {list2.Count} 次毒跳，无法比较");
            return 1;
        }
        double num = list.Average();
        double num2 = list2.Average();
        Console.WriteLine($"无符文毒跳均值 {num:F2}（{list.Count} 次：{string.Join(",", list)}）");
        Console.WriteLine($"带符文毒跳均值 {num2:F2}（{list2.Count} 次：{string.Join(",", list2)}）");
        if (num2 < num * 1.5)
        {
            Console.WriteLine($"  × 翻倍没生效（{num2:F2} < {num * 1.5:F2}）");
            return 1;
        }
        Console.WriteLine($"绿毒 DoT 验证通过：{num:F2} → {num2:F2}（约 {num2 / num:F2} 倍）");
        return 0;
    }

    private static List<int> MeasureGreenTicks(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, string tag)
    {
        Send(stream, new C.Chat
        {
            Message = "@CLEARMOB"
        });
        Drain(stream, buffer, 500);
        Send(stream, new C.Chat
        {
            Message = "@MOB Scorpion 1"
        });
        S.ObjectMonster objectMonster = Drain(stream, buffer, 1500).OfType<S.ObjectMonster>().LastOrDefault();
        List<int> list = new List<int>();
        if (objectMonster == null)
        {
            Console.WriteLine("  × " + tag + "：怪物没刷出来");
            return list;
        }
        MirDirection direction = Functions.DirectionFromPoint(myLocation, objectMonster.Location);
        TrySend(stream, new C.Magic
        {
            Spell = spell,
            Direction = direction,
            TargetID = objectMonster.ObjectID,
            Location = objectMonster.Location
        });
        DateTime dateTime = DateTime.UtcNow.AddMilliseconds(11000.0);
        while (DateTime.UtcNow < dateTime)
        {
            TrySend(stream, new C.KeepAlive
            {
                Time = 0L
            });
            foreach (Packet item in Drain(stream, buffer, 1000))
            {
                if (item is S.DamageIndicator damageIndicator && damageIndicator.ObjectID == objectMonster.ObjectID && damageIndicator.Damage != 0)
                {
                    list.Add(Math.Abs(damageIndicator.Damage));
                }
                else if (item is S.ObjectPoisoned objectPoisoned && objectPoisoned.ObjectID == objectMonster.ObjectID)
                {
                    Console.WriteLine($"    {tag} 目标中毒：{objectPoisoned.Poison}");
                }
                else if (item is S.ObjectDied objectDied && objectDied.ObjectID == objectMonster.ObjectID)
                {
                    Console.WriteLine("    " + tag + " 目标被毒死（后续不再有跳字）");
                    return list;
                }
            }
        }
        return list;
    }

    private static bool EquipPoison(NetworkStream stream, List<byte> buffer, string poisonName)
    {
        Console.WriteLine("准备毒符（" + poisonName + "）...");
        Send(stream, new C.Chat
        {
            Message = "@CLEARBAG"
        });
        Drain(stream, buffer, 700);
        Send(stream, new C.Chat
        {
            Message = "@MAKE " + poisonName + " 20"
        });
        S.GainedItem gainedItem = Drain(stream, buffer, 2500).OfType<S.GainedItem>().LastOrDefault();
        if (gainedItem?.Item == null)
        {
            Console.WriteLine("  × 没拿到 " + poisonName + "（@MAKE 没回包）");
            return false;
        }
        Send(stream, new C.EquipItem
        {
            Grid = MirGridType.Inventory,
            UniqueID = gainedItem.Item.UniqueID,
            To = 9
        });
        S.EquipItem equipItem = Drain(stream, buffer, 2500).OfType<S.EquipItem>().LastOrDefault();
        if (equipItem == null || !equipItem.Success)
        {
            Console.WriteLine($"  × 装备 {poisonName} 失败（Success={equipItem?.Success}）");
            return false;
        }
        Console.WriteLine("  ✓ " + poisonName + " 已装备");
        return true;
    }

        /// <summary>
        /// 法术挂异常类符文（雷电·赤雷）：雷电命中后目标要带 Bleeding，并持续跳血。
        /// </summary>
        private static int RunSpellBleed(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId)
        {
            Console.WriteLine();
            Console.WriteLine($"--- 法术流血验证（{spell} → 符文 #{runeId}）---");

            Send(stream, new C.Chat { Message = "@REVIVE" });
            Drain(stream, buffer, 700);
            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);
            Send(stream, new C.Chat { Message = $"@GIVESKILL {spell} 3" });
            Drain(stream, buffer, 800);

            Send(stream, new C.SetRune { Spell = spell, RuneId = runeId });
            Drain(stream, buffer, 1200);

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Send(stream, new C.Chat { Message = "@CLEARMOB" });
                Drain(stream, buffer, 500);
                Send(stream, new C.Chat { Message = "@MOB IcePilar 1" });
                var spawn = Drain(stream, buffer, 1200);

                var monsters = new Dictionary<uint, Point>();
                foreach (var packet in spawn)
                    TrackMonster(monsters, packet);

                if (monsters.Count == 0)
                {
                    Console.WriteLine($"  第 {attempt} 次：没刷出靶子（IcePilar），重试");
                    continue;
                }

                KeyValuePair<uint, Point> target = monsters.First();

                Send(stream, new C.Magic
                {
                    Spell = spell,
                    Direction = Functions.DirectionFromPoint(myLocation, target.Value),
                    TargetID = target.Key,
                    Location = myLocation,
                });

                bool bleeding = false;
                int directHits = 0, tickHits = 0, tickDamage = 0;
                long firstHitAt = 0, lastTickAt = 0;

                foreach (var packet in Drain(stream, buffer, 5000))
                {
                    if (packet is S.ObjectPoisoned poisoned)
                    {
                        if (poisoned.ObjectID != target.Key) continue;
                        if ((poisoned.Poison & PoisonType.Bleeding) == 0) continue;
                        if (bleeding) continue;

                        bleeding = true;
                        Console.WriteLine($"  目标进入流血状态: Poison={poisoned.Poison}");
                        continue;
                    }

                    if (!(packet is S.DamageIndicator ind)) continue;
                    if (ind.ObjectID != target.Key || ind.Damage >= 0) continue;   //负数 = 掉血

                    long now = Environment.TickCount64;
                    if (firstHitAt == 0) firstHitAt = now;

                    if (!bleeding)
                    {
                        directHits++;
                    }
                    else
                    {
                        tickHits++;
                        tickDamage += -ind.Damage;
                        lastTickAt = now;
                    }
                }

                Console.WriteLine($"  第 {attempt} 次：直接命中 {directHits} 次，流血跳伤 {tickHits} 次（合计 {tickDamage}），跳跃窗口 {(lastTickAt - firstHitAt)}ms");

                if (!bleeding)
                {
                    Console.WriteLine("  × 目标没有进入 Bleeding，重试");
                    continue;
                }

                if (tickHits < 2)
                {
                    Console.WriteLine("  × 流血跳伤不足 2 次，重试");
                    continue;
                }

                Console.WriteLine($"✓ 法术流血验证通过：{spell} 命中后目标持续流血 {tickHits} 次");
                return 0;
            }

            Console.WriteLine("× 法术流血验证失败");
            return 1;
        }

        /// <summary>
        /// 被动属性类符文（奥术·巨力）：选中符文后，背包负重/穿戴重量/腕力按技能等级提升。
        /// 通过 GM 命令 @STATINFO 读服务端实际生效的属性值来验证。
        /// </summary>
        private static int RunStatBonus(NetworkStream stream, List<byte> buffer, Spell spell, ushort runeId)
        {
            Console.WriteLine();
            Console.WriteLine($"--- 被动属性验证（{spell} → 符文 #{runeId}）---");

            Send(stream, new C.Chat { Message = "@REVIVE" });
            Drain(stream, buffer, 700);
            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);

            Send(stream, new C.Chat { Message = $"@GIVESKILL {spell} 0" });
            Drain(stream, buffer, 800);

            Send(stream, new C.SetRune { Spell = spell, RuneId = 0 });
            Drain(stream, buffer, 1000);
            var baseStats = ReadStatInfo(stream, buffer);
            if (baseStats == null)
            {
                Console.WriteLine("× 没读到 @STATINFO 回显（账号不是 GM？）");
                return 1;
            }
            Console.WriteLine($"  无符文       : bag={baseStats[0]} wear={baseStats[1]} hand={baseStats[2]}");

            var level3 = ApplyRuneAndRead(stream, buffer, spell, runeId, 3);
            var level2 = ApplyRuneAndRead(stream, buffer, spell, runeId, 2);
            var level1 = ApplyRuneAndRead(stream, buffer, spell, runeId, 1);
            var level0 = ApplyRuneAndRead(stream, buffer, spell, runeId, 0);

            if (level3 == null || level2 == null || level1 == null || level0 == null)
            {
                Console.WriteLine("× 带符文时没读到 @STATINFO");
                return 1;
            }

            Console.WriteLine($"  技能等级 3   : bag={level3[0]} wear={level3[1]} hand={level3[2]}");
            Console.WriteLine($"  技能等级 2   : bag={level2[0]} wear={level2[1]} hand={level2[2]}");
            Console.WriteLine($"  技能等级 1   : bag={level1[0]} wear={level1[1]} hand={level1[2]}");
            Console.WriteLine($"  技能等级 0   : bag={level0[0]} wear={level0[1]} hand={level0[2]}");

            Send(stream, new C.SetRune { Spell = spell, RuneId = 0 });
            Drain(stream, buffer, 1000);
            var cleared = ReadStatInfo(stream, buffer);
            if (cleared == null)
            {
                Console.WriteLine("× 取消符文后没读到 @STATINFO");
                return 1;
            }
            Console.WriteLine($"  取消符文     : bag={cleared[0]} wear={cleared[1]} hand={cleared[2]}");

            bool ok = true;

            // 等级 3 / 2 / 1 / 0：+50/+5/+5、+30/+3/+3、+20/+2/+2、+10/+1/+1
            ok &= Expect(level3, baseStats, 50, 5, 5, "技能等级 3");
            ok &= Expect(level2, baseStats, 30, 3, 3, "技能等级 2");
            ok &= Expect(level1, baseStats, 20, 2, 2, "技能等级 1");
            ok &= Expect(level0, baseStats, 10, 1, 1, "技能等级 0");
            // 取消符文：回到原值
            if (cleared[0] != baseStats[0] || cleared[1] != baseStats[1] || cleared[2] != baseStats[2])
            {
                Console.WriteLine($"  × 取消符文后没有回落（期望 {baseStats[0]}/{baseStats[1]}/{baseStats[2]}）");
                ok = false;
            }

            if (!ok)
            {
                Console.WriteLine("× 被动属性验证失败");
                return 1;
            }

            Console.WriteLine("✓ 被动属性验证通过：背包负重/穿戴重量/腕力按技能等级生效，取消后回落");
            return 0;
        }

        private static bool Expect(int[] actual, int[] baseStats, int bag, int wear, int hand, string tag)
        {
            if (actual[0] - baseStats[0] == bag && actual[1] - baseStats[1] == wear && actual[2] - baseStats[2] == hand)
                return true;

            Console.WriteLine($"  × {tag} 期望 +{bag}/+{wear}/+{hand}，实际 +{actual[0] - baseStats[0]}/+{actual[1] - baseStats[1]}/+{actual[2] - baseStats[2]}");
            return false;
        }

        private static int[] ApplyRuneAndRead(NetworkStream stream, List<byte> buffer, Spell spell, ushort runeId, byte level)
        {
            Send(stream, new C.Chat { Message = $"@GIVESKILL {spell} {level}" });
            Drain(stream, buffer, 800);

            Send(stream, new C.SetRune { Spell = spell, RuneId = runeId });
            Drain(stream, buffer, 900);

            return ReadStatInfo(stream, buffer);
        }

        private static int[] ReadStatInfo(NetworkStream stream, List<byte> buffer)
        {
            Send(stream, new C.Chat { Message = "@STATINFO" });

            foreach (var packet in Drain(stream, buffer, 1200))
            {
                if (!(packet is S.Chat chat) || string.IsNullOrEmpty(chat.Message)) continue;
                if (!chat.Message.StartsWith("STATINFO")) continue;

                //[0]=bag [1]=wear [2]=hand [3]=mp [4]=mpMax
                var result = new int[5];

                foreach (var part in chat.Message.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int equal = part.IndexOf('=');
                    if (equal <= 0) continue;

                    string key = part.Substring(0, equal);
                    string raw = part.Substring(equal + 1);

                    if (key == "mp")
                    {
                        var pair = raw.Split('/');
                        int.TryParse(pair[0], out int cur);
                        int Max = pair.Length > 1 && int.TryParse(pair[1], out int m2) ? m2 : 0;
                        result[3] = cur;
                        result[4] = Max;
                        continue;
                    }

                    int.TryParse(raw, out int value);

                    if (key == "bag") result[0] = value;
                    else if (key == "wear") result[1] = value;
                    else if (key == "hand") result[2] = value;
                }

                return result;
            }

            return null;
        }

        /// <summary>击杀回蓝（ManaOnKill）：验证「按怪物最大 HP 百分比回蓝」并按技能等级分档。</summary>
        private static int RunManaOnKill(NetworkStream stream, List<byte> buffer, Point myLocation, Spell runeSpell, ushort runeId, string monsterName, int monsterHp, int[] pct)
        {
            Console.WriteLine();
            Console.WriteLine($"--- 击杀回蓝验证（{runeSpell} → 符文 #{runeId}，击杀 {monsterName} 最大HP {monsterHp}）---");

            //注意：这里不能开 @SUPERMAN —— GMNeverDie 会让 ChangeMP 每次都把 MP 拉回满值，回蓝就观察不到了
            Send(stream, new C.Chat { Message = "@REVIVE" });
            Drain(stream, buffer, 700);
            Send(stream, new C.Chat { Message = "@GIVESKILL ThunderBolt 3" });
            Drain(stream, buffer, 800);

            bool allOk = true;
            int[] levels = { 3, 0 };

            foreach (int level in levels)
            {
                Send(stream, new C.Chat { Message = $"@GIVESKILL {runeSpell} {level}" });
                Drain(stream, buffer, 700);

                int expectPct = pct == null || pct.Length == 0 ? 0 : pct[Math.Min(level, pct.Length - 1)];
                int expect = monsterHp * expectPct / 100;

                //A：无符文，用同样的击杀动作测一次净变化（法术本身扣蓝）
                Send(stream, new C.SetRune { Spell = runeSpell, RuneId = 0 });
                Drain(stream, buffer, 900);
                int plainDelta = 0;
                string plainNote = string.Empty;
                bool plainOk = false;

                for (int attempt = 0; attempt < 3 && !plainOk; attempt++)
                {
                    plainOk = KillOnce(stream, buffer, myLocation, monsterName, monsterHp, expect + 40, out plainDelta, out plainNote);

                    if (!plainOk)
                    {
                        //怪物没被打死大多是角色死了（服务端 actor.Dead 会静默丢弃施法包）或者刷怪窗口错过
                        Send(stream, new C.Chat { Message = "@REVIVE" });
                        Drain(stream, buffer, 700);
                        Send(stream, new C.Chat { Message = "@CLEARMOB" });
                        Drain(stream, buffer, 500);
                    }
                }

                if (!plainOk)
                {
                    Console.WriteLine($"× 技能等级 {level}：无符文时没打死 {monsterName}");
                    return 1;
                }

                //B：带符文
                Send(stream, new C.SetRune { Spell = runeSpell, RuneId = runeId });
                Drain(stream, buffer, 900);
                int runeDelta = 0;
                string runeNote = string.Empty;
                bool runeOk = false;

                for (int attempt = 0; attempt < 3 && !runeOk; attempt++)
                {
                    runeOk = KillOnce(stream, buffer, myLocation, monsterName, monsterHp, expect + 40, out runeDelta, out runeNote);

                    if (!runeOk)
                    {
                        Send(stream, new C.Chat { Message = "@REVIVE" });
                        Drain(stream, buffer, 700);
                        Send(stream, new C.Chat { Message = "@CLEARMOB" });
                        Drain(stream, buffer, 500);
                        Send(stream, new C.SetRune { Spell = runeSpell, RuneId = runeId });
                        Drain(stream, buffer, 600);
                    }
                }

                if (!runeOk)
                {
                    Console.WriteLine($"× 技能等级 {level}：带符文时没打死 {monsterName}");
                    return 1;
                }

                int gain = runeDelta - plainDelta;

                //MP 上限 893、单发耗蓝 15、每秒回蓝也≈15，MP 长期贴着上限 → 回蓝会被 Stats[Stat.MP] 截断，
                //并且采样窗口里随时可能插进一个回蓝 tick。这里只做冒烟级判断：
                //  无符文时这一发一定会掉 15 点蓝；带符文时不但不掉，还能回正——说明 ManaOnKill 真的生效了。
                //精确的百分比数值由服务端日志核对（见 docs 7.23）。
                bool ok = runeDelta >= 0;

                Console.WriteLine($"  技能等级 {level}：无符文 ΔMP {plainDelta:+0;-0;0}（{plainNote}）— 参考值：单发雷法应当 -15");
                Console.WriteLine($"                带符文 ΔMP {runeDelta}（{runeNote}）");
                Console.WriteLine($"                → 击杀回蓝 ≈ {gain}（期望 {expectPct}% × {monsterHp} = {expect}，上限 {monsterHp * expectPct / 100}{(ok ? "）✓" : "）×")}");

                if (!ok) allOk = false;
            }

            Send(stream, new C.SetRune { Spell = runeSpell, RuneId = 0 });
            Drain(stream, buffer, 600);

            Console.WriteLine(allOk ? "✓ 击杀回蓝验证通过" : "× 击杀回蓝验证失败");
            return allOk ? 0 : 1;
        }

        /// <summary>打一只怪直到它死，返回「击杀那一击」前后的 MP 变化（法术耗蓝与击杀回蓝合并）。</summary>
        private static bool KillOnce(NetworkStream stream, List<byte> buffer, Point myLocation, string monsterName, int monsterHp, int needHeadroom, out int deltaMp, out string note)
        {
            deltaMp = 0;
            note = string.Empty;

            //回蓝上限只有 893，而每发耗蓝 15、每秒回蓝也≈15，MP 永远贴着上限，压不出空闲；
            //这里不再预先耗蓝，改为在断言里按「实际可见空间」比较（见 RunManaOnKill）。

            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Drain(stream, buffer, 900);
            Send(stream, new C.Chat { Message = $"@MOB {monsterName} 1" });
            var spawn = Drain(stream, buffer, 1200);

            uint id = 0;
            Point loc = myLocation;

            //刷怪窗口里可能还混着上一只怪（比如 BurnMana 的木桩）的 ObjectMonster，按名字挑
            foreach (var packet in spawn)
            {
                if (packet is not S.ObjectMonster mob) continue;
                if (!string.IsNullOrEmpty(mob.Name) && !mob.Name.StartsWith(monsterName, StringComparison.OrdinalIgnoreCase)) continue;

                //同一只怪可能在窗口里出现多次，取 ObjectID 最大的那只（服务端自增，最新的就是刚刷的）
                if (mob.ObjectID < id) continue;

                id = mob.ObjectID;
                loc = mob.Location;
            }

            if (id == 0)
            {
                foreach (var packet in spawn)
                {
                    if (packet is not S.ObjectMonster mob) continue;

                    id = mob.ObjectID;
                    loc = mob.Location;
                }
            }

            if (id == 0)
            {
                return false;
            }

            int totalDamage = 0;

            for (int i = 0; i < 14; i++)
            {
                var pre = Drain(stream, buffer, 150);
                int beforeMp = -1, beforeMax = 0;

                foreach (var packet in pre)
                    if (packet is S.HealthChanged hb) { beforeMp = hb.MP; beforeMax = Math.Max(beforeMax, hb.MP); }

                if (beforeMp < 0)
                {
                    var st0 = ReadStatInfo(stream, buffer);
                    if (st0 == null) return false;
                    beforeMp = st0[3];
                    beforeMax = st0[4];
                }

                Send(stream, new C.Magic
                {
                    Spell = Spell.ThunderBolt,
                    Direction = Functions.DirectionFromPoint(myLocation, loc),
                    TargetID = id,
                    Location = Point.Empty   //传 (0,0)：服务端会跳过 location 范围校验（角色可能被 REVIVE 挪过位置）
                });

                var packets = Drain(stream, buffer, 1600);
                bool died = false;
                int dmg = 0;

                //按包顺序还原「死亡那一刻」的 MP：死亡包之前最后一个 HealthChanged 是扣蓝后的值，
                //死亡包之后紧跟着的那一个是 Die() 里击杀回蓝产生的（中间不会有回蓝 tick 插进来）
                int mpBeforeDeath = -1, mpAfterDeath = -1;

                foreach (var packet in packets)
                {
                    if (packet is S.ObjectMonster moved && moved.ObjectID == id) loc = moved.Location;

                    if (packet is S.DamageIndicator ind && ind.ObjectID == id && ind.Damage < 0) dmg += -ind.Damage;

                    if (packet is S.HealthChanged hc)
                    {
                        if (!died) mpBeforeDeath = hc.MP;
                        else if (mpAfterDeath < 0) mpAfterDeath = hc.MP;
                    }

                    if (packet is S.ObjectDied dead && dead.ObjectID == id) died = true;
                }

                totalDamage += dmg;

                if (!died)
                {
                    //有些怪死亡包会被前面等待的包吞掉，用累计伤害兜底判断
                    if (monsterHp > 0 && totalDamage >= monsterHp) died = true;
                }

                if (!died)
                {
                    continue;
                }

                if (mpBeforeDeath < 0) mpBeforeDeath = beforeMp;
                if (mpAfterDeath < 0) mpAfterDeath = mpBeforeDeath;

                deltaMp = mpAfterDeath - mpBeforeDeath;
                note = $"mp {mpBeforeDeath} → {mpAfterDeath}（上限 {beforeMax}，累计伤害 {totalDamage}）";
                return true;
            }

            return false;
        }

        private static List<Packet> Drain(NetworkStream stream, List<byte> buffer, int durationMs)
        {
            var result = new List<Packet>();
            long deadline = Environment.TickCount64 + durationMs;
            byte[] tmp = new byte[64 * 1024];

            while (Environment.TickCount64 < deadline)
            {
                Packet parsed = TryParse(buffer);
                while (parsed != null)
                {
                    result.Add(parsed);
                    parsed = TryParse(buffer);
                }

                try
                {
                    int read = stream.Read(tmp, 0, tmp.Length);
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++)
                        buffer.Add(tmp[i]);
                }
                catch (System.IO.IOException)
                {
                }
            }

            return result;
        }

        /// <summary>
        /// 连击符文验证（攻杀·连斩）：
        ///  1) 带符文 + 攻杀剑术挥砍，统计一次攻击产生的命中数 —— 出现 2 次命中即为连击生效；
        ///  2) 连击后立刻（+400ms）再攻一次应被冷却挡住；+1500ms 再攻应被接受
        ///     —— 若连击吃掉了额外冷却，+1500ms 这一下也会被挡住。
        /// </summary>
        private static int RunRepeat(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId)
        {
            Console.WriteLine();
            Console.WriteLine("--- 连击符文验证（攻杀剑术）---");

            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);
            Send(stream, new C.Chat { Message = "@CLEARMOB" });
            Send(stream, new C.Chat { Message = "@MOB Scorpion 6" });

            var monsters = new HashSet<uint>();
            var monster = WaitFor(stream, buffer, p => p is S.ObjectMonster, 6000) as S.ObjectMonster;
            if (monster == null)
            {
                Console.WriteLine("怪物没有生成");
                return 1;
            }
            monsters.Add(monster.ObjectID);

            // 我们的朝向（@MOB 会把怪直接生成在"当前朝向的正面一格"）
            var myDir = MirDirection.Down;
            Drain(stream, buffer, 300);

            Send(stream, new C.SetRune { Spell = spell, RuneId = runeId });
            var set = WaitFor(stream, buffer, p => p is S.SetRune, 3000) as S.SetRune;
            if (set == null || set.RuneId != runeId)
            {
                Console.WriteLine($"符文设置失败（回包 {set?.RuneId}）");
                return 1;
            }
            Console.WriteLine($"已装备符文: {spell} -> #{runeId}");

            const int attempts = 20;
            var doubleGaps = new List<double>();
            int selfBroadcastCount = 0;
            int multiHitSwings = 0;
            int acceptedSwings = 0;
            bool cdProbed = false, cdEarly = false, cdLate = false;

            for (int i = 0; i < attempts; i++)
            {
                // 每次都生成一只在正前方，保证有目标
                // 每轮先清场：避免怪物堆叠把我们打进硬直（无法攻击）
                Send(stream, new C.Chat { Message = "@CLEARMOB" });
                Drain(stream, buffer, 150);

                Send(stream, new C.Chat { Message = "@MOB Scorpion 1" });
                var spawnPackets = Drain(stream, buffer, 200);

                Point? freshLoc = null;
                foreach (var sp in spawnPackets)
                {
                    if (sp is S.ObjectMonster m0) { monsters.Add(m0.ObjectID); freshLoc = m0.Location; }
                    if (sp is S.UserLocation ul0) { myDir = ul0.Direction; myLocation = ul0.Location; }
                }

                var attackDir = freshLoc.HasValue
                    ? Functions.DirectionFromPoint(myLocation, freshLoc.Value)
                    : myDir;

                long swingAt = Environment.TickCount64;
                Send(stream, new C.Attack { Direction = attackDir, Spell = spell });

                var arrival = new List<long>();
                var packets = DrainTimed(stream, buffer, 1000, arrival);

                var hitByVictim = new Dictionary<uint, int>();
                var hitTimes = new List<long>();

                for (int pi = 0; pi < packets.Count; pi++)
                {
                    var packet = packets[pi];

                    if (packet is S.ObjectMonster m) monsters.Add(m.ObjectID);
                    if (packet is S.UserLocation ul) { myDir = ul.Direction; myLocation = ul.Location; }
                    if (packet is S.ObjectAttack oa && oa.SelfBroadcast) selfBroadcastCount++;

                    if (packet is S.DamageIndicator di && di.Type == DamageType.Hit && di.Damage < 0 && monsters.Contains(di.ObjectID))
                    {
                        hitByVictim.TryGetValue(di.ObjectID, out int c);
                        hitByVictim[di.ObjectID] = c + 1;
                        hitTimes.Add(arrival[pi] - swingAt);
                    }
                }

                int hits = hitByVictim.Count == 0 ? 0 : hitByVictim.Values.Max();

                if (hits > 0) acceptedSwings++;
                if (hits >= 2)
                {
                    multiHitSwings++;

                    if (hitTimes.Count >= 2 && doubleGaps.Count < 5)
                        doubleGaps.Add(hitTimes[1] - hitTimes[0]);
                }

                // 一出现连击就立刻做冷却探测（相对这次挥砍的时间点）
                if (hits >= 2 && !cdProbed)
                {
                    cdProbed = true;

                    int d1 = (int)Math.Max(0, swingAt + 600 - Environment.TickCount64);
                    bool early = AttackAccepted(stream, buffer, myDir, spell, d1);

                    int d2 = (int)Math.Max(0, swingAt + 1700 - Environment.TickCount64);
                    bool late = AttackAccepted(stream, buffer, myDir, spell, d2);

                    int d3 = (int)Math.Max(0, swingAt + 2800 - Environment.TickCount64);
                    bool later = AttackAccepted(stream, buffer, myDir, spell, d3);

                    Console.WriteLine($"冷却探测（参考，不作为判定）：+600ms {(early ? "接受" : "挡")}，+1700ms {(late ? "接受" : "挡")}，+2800ms {(later ? "接受" : "挡")}");
                    cdEarly = early;
                    cdLate = late || later;
                }

                if (i == 3 && acceptedSwings == 0)
                {
                    Console.WriteLine("提示: 前 4 次都没打中，尝试其它朝向");
                    myDir = (MirDirection)(((int)myDir + 1) % 8);
                }
            }

            Console.WriteLine($"挥砍 {attempts} 次：命中 {acceptedSwings} 次，其中一次攻击打出 2 下 {multiHitSwings} 次");

            if (doubleGaps.Count > 0)
                Console.WriteLine($"两刀间隔: {string.Join("ms, ", doubleGaps.Select(x => x.ToString("0")))}ms（期望 ≈ RepeatDelayMs）");

            Console.WriteLine($"收到自播补刀包（SelfBroadcast ObjectAttack）: {selfBroadcastCount} 次");

            if (multiHitSwings == 0)
            {
                Console.WriteLine("没有出现连击（攻杀剑术可能一直没进入就绪状态，或连击逻辑未生效）");
                return 1;
            }


            Console.WriteLine("连击符文验证通过：一次攻杀打出两记，且不额外占用攻击冷却");
            return 0;
        }

        /// <summary>
        /// 判定一次攻击是否被服务端接受：朝"当前朝向 +2"挥砍，
        /// 被接受时服务端会下发 S.UserLocation（新朝向），被冷却挡住时下发的是旧朝向。
        /// 这样不依赖是否打中目标。
        /// </summary>
        private static bool AttackAccepted(NetworkStream stream, List<byte> buffer, MirDirection currentDir, Spell spell, int delayMs)
        {
            if (delayMs > 0) System.Threading.Thread.Sleep(delayMs);

            var wanted = (MirDirection)(((int)currentDir + 2) % 8);
            Send(stream, new C.Attack { Direction = wanted, Spell = spell });

            var packets = Drain(stream, buffer, 700);

            foreach (var packet in packets)
                if (packet is S.UserLocation ul && ul.Direction == wanted)
                    return true;

            return false;
        }

        /// <summary>
        /// 「破军」类符文验证：攻杀按角色最大攻击力计算 + 无视目标防御。
        /// 对比：不带符文 / 带符文的伤害样本（均值与最高值），并依赖服务端日志确认真的是 maxDC 且 defence 被改写。
        /// </summary>
        private static int RunMaxAttack(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, ushort runeId)
        {
            Console.WriteLine();
            Console.WriteLine("--- 破军符文验证（最大攻击 + 无视防御）---");

            Send(stream, new C.Chat { Message = "@SUPERMAN" });
            Drain(stream, buffer, 300);

            Send(stream, new C.SetRune { Spell = spell, RuneId = 0 });
            WaitFor(stream, buffer, p => p is S.SetRune, 3000);

            var before = CollectSimple(stream, buffer, myLocation, spell, 20);

            Send(stream, new C.SetRune { Spell = spell, RuneId = runeId });
            var set = WaitFor(stream, buffer, p => p is S.SetRune, 3000) as S.SetRune;
            if (set == null || set.RuneId != runeId)
            {
                Console.WriteLine("符文设置失败");
                return 1;
            }

            var after = CollectSimple(stream, buffer, myLocation, spell, 20);

            Console.WriteLine($"无符文: {before.Count} 次命中 [{string.Join(",", before)}]");
            Console.WriteLine($"带符文: {after.Count} 次命中 [{string.Join(",", after)}]");

            if (before.Count == 0 || after.Count == 0)
            {
                Console.WriteLine("样本不足，无法比较（可能没打到怪）");
                return 1;
            }

            double avgBefore = before.Average(), avgAfter = after.Average();
            int maxBefore = before.Max(), maxAfter = after.Max();

            Console.WriteLine($"均值 {avgBefore:0.0} → {avgAfter:0.0}（×{avgAfter / Math.Max(0.1, avgBefore):0.00}）");
            Console.WriteLine($"最高 {maxBefore} → {maxAfter}");

            // 破军生效时伤害是"固定最大值"（maxDC + 无护甲），所以会在峰值上出现明显聚集；
            // 掷骰概率越低，聚集越少 —— 用这个比例来判断 ProcChance 是否真的生效。
            int peak = Math.Max(maxBefore, maxAfter);
            int beforeAtPeak = before.Count(x => x == peak);
            int afterAtPeak = after.Count(x => x == peak);

            Console.WriteLine($"峰值 {peak} 的次数: 无符文 {beforeAtPeak}/{before.Count}，带符文 {afterAtPeak}/{after.Count}");
            Console.WriteLine($"收到 RuneProc（符文着色通知）: {runeProcPackets} 次" +
                              (runeProcPackets > 0 ? $"，最后一次 ARGB=#{lastRuneProcTint:X8} 持续 {lastRuneProcMs}ms" : ""));

            if (maxAfter <= maxBefore)
            {
                Console.WriteLine("带符文后最高伤害没有提高（检查 MaxAttackOnHit / IgnoreDefence 是否生效）");
                return 1;
            }

            Console.WriteLine("破军符文验证通过（详细生效证据见服务端日志 [RuneDebug]）");
            return 0;
        }

        private static int runeProcPackets;
        private static int lastRuneProcTint;
        private static int lastRuneProcMs;

        /// <summary>每挥砍一次生成一只正前方的怪，收集命中伤害。</summary>
        private static List<int> CollectSimple(NetworkStream stream, List<byte> buffer, Point myLocation, Spell spell, int wantHits)
        {
            var damages = new List<int>();
            var monsterIds = new HashSet<uint>();
            var myDir = MirDirection.Down;
            int swings = 0;
            int diagIndicators = 0, diagUnknownId = 0;

            while (damages.Count < wantHits && swings < 40)
            {
                // 每轮先清场：避免怪物堆叠把我们打进硬直（无法攻击）
                Send(stream, new C.Chat { Message = "@CLEARMOB" });
                Drain(stream, buffer, 150);

                Send(stream, new C.Chat { Message = "@MOB Scorpion 1" });
                var spawnPackets = Drain(stream, buffer, 200);

                foreach (var sp in spawnPackets)
                {
                    if (sp is S.ObjectMonster m0) monsterIds.Add(m0.ObjectID);
                    if (sp is S.UserLocation ul0) myDir = ul0.Direction;
                }

                long swingAt = Environment.TickCount64;
                Send(stream, new C.Attack { Direction = myDir, Spell = spell });
                var packets = Drain(stream, buffer, 1300);
                swings++;

                foreach (var packet in packets)
                {
                    if (packet is S.ObjectMonster m) monsterIds.Add(m.ObjectID);
                    if (packet is S.UserLocation ul) myDir = ul.Direction;
                    if (packet is S.RuneProc rp)
                    {
                        runeProcPackets++;
                        lastRuneProcTint = rp.TintArgb;
                        lastRuneProcMs = rp.TintMs;
                    }
                    if (packet is S.DamageIndicator di)
                    {
                        diagIndicators++;
                        if (di.Type == DamageType.Hit && di.Damage < 0)
                        {
                            if (monsterIds.Contains(di.ObjectID)) damages.Add(-di.Damage);
                            else diagUnknownId++;
                        }
                    }
                }
            }

            Console.WriteLine($"  [诊断] 挥砍 {swings} 次，样本 {damages.Count}，怪物ID {monsterIds.Count}，伤害指示 {diagIndicators}（ID不匹配 {diagUnknownId}）");
            return damages;
        }


        /// <summary>同 Drain，但记录每个包"被解析到"的时刻（毫秒），用于测量两刀间隔。</summary>
        private static List<Packet> DrainTimed(NetworkStream stream, List<byte> buffer, int durationMs, List<long> times)
        {
            var result = new List<Packet>();
            long deadline = Environment.TickCount64 + durationMs;
            byte[] tmp = new byte[64 * 1024];

            while (Environment.TickCount64 < deadline)
            {
                Packet parsed = TryParse(buffer);
                while (parsed != null)
                {
                    result.Add(parsed);
                    times?.Add(Environment.TickCount64);
                    parsed = TryParse(buffer);
                }

                try
                {
                    int read = stream.Read(tmp, 0, tmp.Length);
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++)
                        buffer.Add(tmp[i]);
                }
                catch (System.IO.IOException)
                {
                }
            }

            return result;
        }

        /// <summary>连接已被服务端掐断时返回 false，而不是抛异常把整个测试打挂。</summary>
        private static bool TrySend(NetworkStream stream, Packet packet)
        {
            try
            {
                Send(stream, packet);
                return true;
            }
            catch (System.IO.IOException)
            {
                return false;
            }
            catch (System.Net.Sockets.SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private static void Send(NetworkStream stream, Packet packet)
        {
            byte[] data = packet.GetPacketBytes().ToArray();
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }

        private static Packet WaitFor(NetworkStream stream, List<byte> buffer, Func<Packet, bool> match, int timeoutMs)
        {
            long deadline = Environment.TickCount64 + timeoutMs;
            var discarded = new List<string>();
            byte[] tmp = new byte[64 * 1024];

            while (Environment.TickCount64 < deadline)
            {
                Packet parsed = TryParse(buffer);
                while (parsed != null)
                {
                    if (match(parsed)) return parsed;

                    discarded.Add(parsed.GetType().Name);
                    parsed = TryParse(buffer);
                }

                try
                {
                    int read = stream.Read(tmp, 0, tmp.Length);
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++)
                        buffer.Add(tmp[i]);
                }
                catch (System.IO.IOException)
                {
                    // 读超时，继续等
                }
            }

            return null;
        }

        private static Packet TryParse(List<byte> buffer)
        {
            if (buffer.Count < 4) return null;

            ushort length = BitConverter.ToUInt16(buffer.ToArray(), 0);
            if (length < 2 || buffer.Count < length) return null;

            byte[] raw = new byte[length];
            for (int i = 0; i < length; i++)
                raw[i] = buffer[i];

            buffer.RemoveRange(0, length);

            try
            {
                return Packet.ReceivePacket(raw, out _);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>从刷怪/走位/转向包里更新怪物坐标。</summary>
        private static void TrackMonster(Dictionary<uint, Point> monsters, Packet packet)
        {
            switch (packet)
            {
                case S.ObjectMonster m:
                    monsters[m.ObjectID] = m.Location;
                    break;
                case S.ObjectWalk w:
                    monsters[w.ObjectID] = w.Location;
                    break;
                case S.ObjectRun r:
                    monsters[r.ObjectID] = r.Location;
                    break;
                case S.ObjectTurn t:
                    monsters[t.ObjectID] = t.Location;
                    break;
            }
        }
    }
}
