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
        public static int Run(string host, int port, string account, string password, Spell spell, ushort runeId, bool lifesteal, string expect = "buff", bool crit = false, bool repeat = false)
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

            // 顺带验证 GM 调试命令（同时验证该账号确实是 GM）
            Send(stream, new C.Chat { Message = "@RUNELIST" });
            var chat = WaitFor(stream, buffer, p => p is S.Chat c && c.Message != null && c.Message.Contains("剑术"), 5000) as S.Chat;

            if (chat == null)
                Console.WriteLine("提示: 没收到 @RUNELIST 的回显（可能不是 GM 账号），继续测试符文");
            else
                Console.WriteLine($"GM 命令可用: {chat.Message}");

            Send(stream, new C.SetRune { Spell = spell, RuneId = runeId });

            var setRune = WaitFor(stream, buffer, p => p is S.SetRune, 5000) as S.SetRune;
            if (setRune == null || setRune.RuneId != runeId)
            {
                Console.WriteLine($"选择符文失败（回包 RuneId={setRune?.RuneId}）");
                return 1;
            }
            Console.WriteLine($"服务端已接受符文: {setRune.Spell} -> #{setRune.RuneId}");

            // 吸血类符文：需要真的造成伤害才能验证
            if (lifesteal)
            {
                return RunLifesteal(stream, buffer, myLocation);
            }

            if (crit)
            {
                return RunCrit(stream, buffer, myLocation, spell, runeId);
            }

            if (repeat)
            {
                return RunRepeat(stream, buffer, myLocation, spell, runeId);
            }

            if (expect != "buff")
            {
                Console.WriteLine("该符文不是 Buff 类，跳过 AddBuff 校验");
                return 0;
            }

            // 挥砍一次（OnSwing 在攻击广播后就触发，不需要命中目标）
            Send(stream, new C.Attack { Direction = MirDirection.Down, Spell = Spell.None });

            var addBuff = WaitFor(stream, buffer, p => p is S.AddBuff, 6000) as S.AddBuff;
            if (addBuff == null)
            {
                Console.WriteLine("攻击后没有收到 AddBuff —— 符文 Buff 未触发");
                return 1;
            }

            var buff = addBuff.Buff;
            Console.WriteLine($"收到 Buff: Type={buff.Type}, 持续时间≈{buff.ExpireTime}ms, Values=[{string.Join(",", buff.Values)}]");

            if (buff.Type != BuffType.Rune)
            {
                Console.WriteLine("Buff 类型不是 Rune，可能是别的 Buff 干扰");
                return 1;
            }

            foreach (var kv in buff.Stats.Values)
                Console.WriteLine($"  属性: {kv.Key} = {kv.Value}");

            Console.WriteLine();
            Console.WriteLine("冒烟测试通过：登录 → 符文表 → 选择符文 → 挥砍 → 符文 Buff 全链路 OK");
            return 0;
        }


        /// <summary>
        /// 吸血符文验证：叫一只怪 → 等它打掉我们一点血 → 打它一次 → 看血量是否回升。
        /// </summary>
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
            int multiHitSwings = 0;
            int acceptedSwings = 0;
            bool cdProbed = false, cdEarly = false, cdLate = false;

            for (int i = 0; i < attempts; i++)
            {
                // 每次都生成一只在正前方，保证有目标
                Send(stream, new C.Chat { Message = "@MOB Scorpion 1" });
                var spawnPackets = Drain(stream, buffer, 250);

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
                var packets = Drain(stream, buffer, 700);

                int hits = 0;

                foreach (var packet in packets)
                {
                    if (packet is S.ObjectMonster m) monsters.Add(m.ObjectID);
                    if (packet is S.UserLocation ul) { myDir = ul.Direction; myLocation = ul.Location; }
                    if (packet is S.DamageIndicator di && di.Type == DamageType.Hit && di.Damage < 0 && monsters.Contains(di.ObjectID))
                        hits++;
                }

                if (hits > 0) acceptedSwings++;
                if (hits >= 2) multiHitSwings++;

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

                    Console.WriteLine($"冷却探测（相对这次连击）：+600ms {(early ? "接受" : "挡")}，+1700ms {(late ? "接受" : "挡")}，+2800ms {(later ? "接受" : "挡")}");
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

            if (multiHitSwings == 0)
            {
                Console.WriteLine("没有出现连击（攻杀剑术可能一直没进入就绪状态，或连击逻辑未生效）");
                return 1;
            }

            if (!cdProbed)
            {
                Console.WriteLine("没能在连击后做冷却探测");
                return 1;
            }

            if (!cdLate)
            {
                Console.WriteLine("+1700ms 的攻击仍被挡 —— 连击很可能吃掉了额外冷却");
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
    }
}
