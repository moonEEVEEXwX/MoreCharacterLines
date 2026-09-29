using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

/// <summary>
/// 预检：不启动游戏，验证 MoreCharacterLines 能不能正常工作。
///   1. 加载 MoreCharacterLines.dll（用游戏目录里的 .NET 9 运行时程序集）
///   2. NRestSiteRoom._Ready 挂补丁；反射名字（_runState / Header）对不对
///   3. 就地 lines.json 路径解析
///   4. 多场景：rest_site / ping 都能抽到对应台词
///   5. 条件池：low_hp 生效比例、通用池混合比例
/// </summary>
internal static class Preflight
{
    private static int _failures;

    private static int Main(string[] args)
    {
        string gameDataDir = args.Length > 0
            ? args[0]
            : @"E:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64";
        string modDll = args.Length > 1
            ? args[1]
            : @"D:\sts2modtest\MoreCharacterLines\bin\csc\MoreCharacterLines.dll";

        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string candidate = Path.Combine(gameDataDir, name.Name + ".dll");
            return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
        };

        Assembly mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(modDll);
        Check(true, $"加载 {Path.GetFileName(modDll)}");

        Type? roomType = Type.GetType("MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom, sts2");
        Check(roomType != null, "找到类型 MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom");

        MethodInfo? ready = roomType!.GetMethod("_Ready",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Check(ready != null, "找到方法 NRestSiteRoom._Ready");

        FieldInfo? runState = AccessTools.DeclaredField(roomType, "_runState");
        Check(runState != null, $"找到私有字段 _runState（{runState?.FieldType.FullName}）");

        MethodInfo? headerGetter = AccessTools.PropertyGetter(roomType, "Header");
        Check(headerGetter != null, $"找到私有属性 Header（{headerGetter?.ReturnType.FullName}）");

        // 低血判定：游戏自己的 CharacterModel.IsLowHealth（protected）
        Type? characterType = Type.GetType("MegaCrit.Sts2.Core.Models.CharacterModel, sts2");
        MethodInfo? lowHpGetter = characterType is null ? null : AccessTools.PropertyGetter(characterType, "IsLowHealth");
        Check(lowHpGetter != null, $"找到 CharacterModel.IsLowHealth（{lowHpGetter?.ReturnType.FullName}）→ 复用它作为 low_hp 临界点");

        try
        {
            var harmony = new Harmony("preflight.characterlines");
            harmony.PatchAll(mod);

            Patches? info = Harmony.GetPatchInfo(ready);
            IEnumerable<Patch> postfixes = info?.Postfixes ?? Enumerable.Empty<Patch>();
            Check(postfixes.Any(), $"NRestSiteRoom._Ready 已挂上 postfix（{postfixes.Count()} 个）");
            foreach (Patch patch in postfixes)
            {
                Console.WriteLine($"       来自 {patch.owner} -> {patch.PatchMethod.DeclaringType?.FullName}.{patch.PatchMethod.Name}");
            }

            // ping（多人催促）：气泡改写 + 催促计时 + 调试预览三个挂点都要挂上
            CheckPatched("MegaCrit.Sts2.Core.Multiplayer.Game.FlavorSynchronizer, sts2", "CreateEndTurnPingDialogueIfNecessary");
            CheckPatched("MegaCrit.Sts2.Core.Combat.CombatManager, sts2", "SetReadyToEndTurn");
            CheckPatched("MegaCrit.Sts2.Core.Combat.CombatManager, sts2", "UndoReadyToEndTurn");

            harmony.UnpatchAll("preflight.characterlines");
        }
        catch (Exception e)
        {
            Check(false, "PatchAll 抛异常: " + e.GetType().Name + ": " + e.Message);
        }

        // 台词库
        Type? bank = mod.GetType("MoreCharacterLines.LineBank");
        Check(bank != null, "找到类型 MoreCharacterLines.LineBank");
        bank?.GetField("_loggedSource", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, true); // 别去碰依赖 Godot 的日志

        MethodInfo? pathMethod = bank?.GetMethod("ModFolderFilePath", BindingFlags.NonPublic | BindingFlags.Static);
        string? resolved = pathMethod?.Invoke(null, null) as string;
        string expected = Path.Combine(Path.GetDirectoryName(modDll) ?? ".", "lines.json");
        Check(resolved == expected, $"就地台词文件路径解析 -> {resolved}");

        MethodInfo? pick = bank?.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Pick" && m.GetParameters().Length == 3);
        MethodInfo? pickWithCondition = bank?.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Pick" && m.GetParameters().Length == 4);
        Check(pick != null && pickWithCondition != null, "找到 LineBank.Pick 的两个重载（含 out usedCondition）");
        if (pick is null)
        {
            Check(false, "找不到 LineBank.Pick");
        }
        else
        {
            try
            {
                HashSet<string> normal = Sample(pick, "rest_site", "IRONCLAD", 3000);
                HashSet<string> lowHp = Sample(pick, "rest_site", "IRONCLAD", new[] { "low_hp" }, 3000);

                Check(normal.Count >= 8, $"rest_site/IRONCLAD 能抽到 {normal.Count} 句（4 专属 + 6 通用）");
                Console.WriteLine($"       常规：{string.Join(" / ", normal.Take(3))}");

                // 条件池：low_hp 专属句子只在低血时出现
                List<string> lowOnly = lowHp.Except(normal).ToList();
                Check(lowOnly.Count >= 4, $"low_hp 池多出 {lowOnly.Count} 句专属台词：{string.Join(" / ", lowOnly.Take(3))}");
                Check(!normal.Any(line => lowOnly.Contains(line)), "不满足条件时绝不会抽到 low_hp 的专属台词");

                // 条件池命中率：_chance = 0.8
                int lowHit = 0;
                for (int i = 0; i < 3000; i++)
                {
                    if (pick.Invoke(null, new object?[] { "rest_site", "IRONCLAD", new[] { "low_hp" } }) is string line &&
                        !normal.Contains(line)) lowHit++;
                }
                double lowShare = lowHit / 3000.0;
                Check(lowShare > 0.72 && lowShare < 0.88, $"低血时条件池命中率 ≈ {lowShare:P1}（_chance = 0.8）");

                // 通用池混合比例：_defaultChance = 0.1
                HashSet<string> defaults = Sample(pick, "rest_site", "__NO_SUCH_CHARACTER__", 400);
                int fromDefault = 0;
                for (int i = 0; i < 3000; i++)
                {
                    if (pick.Invoke(null, new object?[] { "rest_site", "IRONCLAD", Array.Empty<string>() }) is string line &&
                        defaults.Contains(line)) fromDefault++;
                }
                double share = fromDefault / 3000.0;
                Check(share > 0.06 && share < 0.16, $"IRONCLAD 抽到通用池比例 ≈ {share:P1}（_defaultChance = 0.1）");

                // 未写专属台词的角色 → DEFAULT
                string? unknown = pick.Invoke(null, new object?[] { "rest_site", "SOME_MODDED_CHARACTER", Array.Empty<string>() }) as string;
                Check(!string.IsNullOrWhiteSpace(unknown), $"未写专属台词的角色回退到 DEFAULT：{unknown}");

                // 多场景：ping 还没接代码，但 JSON 里写了就应该能抽到
                string? ping = pick.Invoke(null, new object?[] { "ping", "IRONCLAD", Array.Empty<string>() }) as string;
                Check(!string.IsNullOrWhiteSpace(ping), $"另一个场景 ping 也能抽到台词：{ping}");

                // 遗物 / 状态条件池：每个都要能抽到，并且报告的 usedCondition 要对
                string[] relicConditions =
                {
                    "candle_low", "girya_maxed", "girya_progress",
                    "relic_shovel", "relic_cleaver", "relic_tent", "relic_dream_catcher",
                    "relic_mailbox", "relic_pillow", "relic_paels_growth",
                    "relic_humidifier", "relic_tea_set", "relic_fake_tea_set",
                };

                var conditionLines = new HashSet<string>();
                int failed = 0;
                foreach (string condition in relicConditions)
                {
                    int hits = 0;
                    string? sample = null;
                    for (int i = 0; i < 500; i++)
                    {
                        object?[] callArgs = { "rest_site", "IRONCLAD", null, new[] { condition } };
                        string? line = pickWithCondition.Invoke(null, callArgs) as string;
                        string? reported = callArgs[2] as string;

                        // 注意：_chance 小于 1 时，大部分尝试会跳过条件池、落回 normal，
                        // 所以要看「报告的 usedCondition 是不是这个条件」，而不是看有没有返回台词。
                        if (reported == condition && line is not null)
                        {
                            hits++;
                            sample ??= line;
                            conditionLines.Add(line);
                        }
                    }

                    if (hits == 0)
                    {
                        Check(false, $"条件 {condition} 从来没被命中（500 次）");
                        failed++;
                        continue;
                    }

                    Console.WriteLine($"       {condition,-20} 命中 {hits,3}/500 -> {sample}");
                }

                Check(failed == 0, $"{relicConditions.Length} 个遗物/状态条件全部可用（能命中且报告正确）");
                Check(!normal.Any(line => conditionLines.Contains(line)), "条件台词不会混进平时随机（平时抽不到它们）");

                // 遗物台词占比：拥有 N 个火堆遗物 → 说遗物台词的概率 ≈ perRelic × N（上限 max）
                double RelicShare(params string[] ownedConditions)
                {
                    const int tries = 3000;
                    int hits = 0;
                    for (int i = 0; i < tries; i++)
                    {
                        object?[] callArgs = { "rest_site", "IRONCLAD", null, ownedConditions };
                        pickWithCondition.Invoke(null, callArgs);
                        if (callArgs[2] as string is string used && used.StartsWith("relic_", StringComparison.Ordinal)) hits++;
                    }
                    return hits / (double)tries;
                }

                double one = RelicShare("relic_shovel");
                double two = RelicShare("relic_shovel", "relic_cleaver");
                double three = RelicShare("relic_shovel", "relic_cleaver", "relic_tent");
                double four = RelicShare("relic_shovel", "relic_cleaver", "relic_tent", "relic_pillow");

                Check(Math.Abs(one - 0.25) < 0.05, $"1 个火堆遗物 → 遗物台词占比 ≈ 25%（实测 {one:P1}）");
                Check(Math.Abs(two - 0.50) < 0.06, $"2 个 → ≈ 50%（实测 {two:P1}）");
                Check(Math.Abs(three - 0.75) < 0.06, $"3 个 → ≈ 75%（实测 {three:P1}）");
                Check(Math.Abs(four - 0.80) < 0.05, $"4 个 → 封顶 ≈ 80%（实测 {four:P1}）");

                // 组内随机：两个遗物都应该有机会被选中，而不是永远说列表里第一个
                var usedPools = new HashSet<string>();
                for (int i = 0; i < 400; i++)
                {
                    object?[] callArgs = { "rest_site", "IRONCLAD", null, new[] { "relic_shovel", "relic_cleaver" } };
                    pickWithCondition.Invoke(null, callArgs);
                    if (callArgs[2] as string is string used && used.StartsWith("relic_", StringComparison.Ordinal)) usedPools.Add(used);
                }
                Check(usedPools.Count == 2, $"遗物组内会随机挑（本轮出现过 {string.Join(", ", usedPools)}）");

                // 优先级：girya_maxed（_chance = 1）排在 relic_shovel（0.15）前面时，永远优先
                const string giryaMaxedLine = "（你看着自己的肌肉，很是满意。）";
                int giryaWins = 0;
                for (int i = 0; i < 30; i++)
                {
                    object?[] callArgs = { "rest_site", "IRONCLAD", null, new[] { "girya_maxed", "relic_shovel" } };
                    if (pickWithCondition.Invoke(null, callArgs) as string == giryaMaxedLine) giryaWins++;
                }
                Check(giryaWins == 30, $"条件优先级：girya_maxed 排在前面时 30/30 优先（实际 {giryaWins}/30）");
            }
            catch (TargetInvocationException e)
            {
                Check(false, "Pick() 抛异常: " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message);
            }
        }

        // ── ping（多人催促）─────────────────────────────────────────────────────
        // 补丁依赖的游戏成员：游戏更新改名 → 这里先炸，而不是玩家在游戏里发现失效
        CheckMember("MegaCrit.Sts2.Core.Multiplayer.Game.FlavorSynchronizer, sts2", "_endTurnPingDialogues");
        CheckMember("MegaCrit.Sts2.Core.Nodes.Vfx.NSpeechBubbleVfx, sts2", "_text");
        CheckMember("MegaCrit.Sts2.Core.Nodes.Vfx.NSpeechBubbleVfx, sts2", "_label");
        CheckMember("MegaCrit.Sts2.Core.Entities.Players.Player, sts2", "NetId");
        CheckMember("MegaCrit.Sts2.Core.Rooms.CombatRoom, sts2", "CombatState");
        CheckMember("MegaCrit.Sts2.Core.Combat.CombatState, sts2", "RoundNumber");
        CheckMember("MegaCrit.Sts2.Core.Combat.CombatState, sts2", "Players");
        CheckMember("MegaCrit.Sts2.Core.Combat.CombatManager, sts2", "IsPlayerReadyToEndTurn");
        CheckMember("MegaCrit.Sts2.Core.Entities.Players.Player, sts2", "RunState");

        // 语气分档（纯函数）：≥50% 人结束回合才开始计时，1 分钟更急、5 分钟更凶
        Type? toneType = mod.GetType("MoreCharacterLines.PingTone");
        MethodInfo? tierMethod = toneType is null ? null : AccessTools.Method(toneType, "Tier");
        MethodInfo? poolsMethod = toneType is null ? null : AccessTools.Method(toneType, "Pools");
        if (tierMethod is null || poolsMethod is null)
        {
            Check(false, "找不到 PingTone.Tier / PingTone.Pools");
        }
        else
        {
            int Tier(int ready, int total, double waited) =>
                (int)tierMethod.Invoke(null, new object?[] { ready, total, waited, 60, 300 })!;

            Check(Tier(1, 2, 0) == 0 && Tier(1, 2, 59.9) == 0, "刚到 50% 还没到 1 分钟 → 正常语气");
            Check(Tier(0, 2, 600) == 0, "没人结束回合 → 不催（哪怕等很久）");
            Check(Tier(1, 4, 600) == 0, "只有 25% 人结束回合 → 不催");
            Check(Tier(1, 2, 60) == 1 && Tier(1, 2, 299) == 1, "满 1 分钟 → 更急（wait_urgent）");
            Check(Tier(1, 2, 300) == 2 && Tier(2, 3, 900) == 2, "满 5 分钟 → 更凶（wait_angry）");

            var pools0 = (string[])poolsMethod.Invoke(null, new object?[] { 0 })!;
            var pools1 = (string[])poolsMethod.Invoke(null, new object?[] { 1 })!;
            var pools2 = (string[])poolsMethod.Invoke(null, new object?[] { 2 })!;
            Check(pools0.Length == 0, "正常档不带条件池（走平时池）");
            Check(pools1.Length == 1 && pools1[0] == "wait_urgent", "更急档 → wait_urgent");
            Check(pools2.Length == 2 && pools2[0] == "wait_angry" && pools2[1] == "wait_urgent",
                "更凶档 → wait_angry 优先，其次 wait_urgent");
        }

        Type? clockType = mod.GetType("MoreCharacterLines.PingClock");
        MethodInfo? halfwayMethod = clockType is null ? null : AccessTools.Method(clockType, "IsHalfway");
        if (halfwayMethod is null)
        {
            Check(false, "找不到 PingClock.IsHalfway");
        }
        else
        {
            bool Half(int ready, int total) => (bool)halfwayMethod.Invoke(null, new object?[] { ready, total })!;
            Check(Half(1, 2) && Half(2, 4) && Half(2, 3) && Half(3, 4), "50% 判定：1/2、2/4、2/3、3/4 都算到点");
            Check(!Half(0, 2) && !Half(1, 3) && !Half(0, 0), "50% 判定：0 人 / 33% / 空玩家列表都不算");
        }

        // 两端一致的种子：回合号 + 催促者 NetId + 档位
        Type? scenePingType = mod.GetType("MoreCharacterLines.ScenePing");
        MethodInfo? seedMethod = scenePingType is null ? null : AccessTools.Method(scenePingType, "Seed");
        MethodInfo? rewriteMethod = scenePingType is null ? null : AccessTools.Method(scenePingType, "Rewrite");
        if (seedMethod is null || rewriteMethod is null)
        {
            Check(false, "找不到 ScenePing.Seed / ScenePing.Rewrite");
        }
        else
        {
            int Seed(int round, object netId, int tier, int pingIndex = 0) =>
                (int)seedMethod.Invoke(null, new object?[] { round, netId, tier, pingIndex })!;

            Check(Seed(3, 76561198000000000L, 0) == Seed(3, 76561198000000000L, 0), "催促种子：同回合 + 同玩家 + 同档位 → 两端口径一致");
            Check(Seed(3, 76561198000000000L, 0) != Seed(3, 76561198000000000L, 1), "催促种子：升档会换一句");
            Check(Seed(3, 76561198000000000L, 0) != Seed(4, 76561198000000000L, 0), "催促种子：换回合会换一句");
            Check(Seed(3, 76561198000000000L, 0) != Seed(3, 111L, 0), "催促种子：不同玩家说的话不同");
            Check(Seed(3, 76561198000000000L, 0, 0) != Seed(3, 76561198000000000L, 0, 1),
                "催促种子：同一档里再按一次会说不同的话（每次换一句）");

            // 「第几次 ping」的计数：两端各自数，升档 / 换回合时重新对齐
            Type? varietyType = mod.GetType("MoreCharacterLines.PingVariety");
            MethodInfo? varietyNext = varietyType is null ? null : AccessTools.Method(varietyType, "Next");
            MethodInfo? varietyReset = varietyType is null ? null : AccessTools.Method(varietyType, "Reset");
            if (varietyNext is null || varietyReset is null)
            {
                Check(false, "找不到 PingVariety.Next / PingVariety.Reset");
            }
            else
            {
                int Next(int round, object netId, int tier) => (int)varietyNext.Invoke(null, new object?[] { round, netId, tier })!;

                varietyReset.Invoke(null, null);
                int first = Next(5, 123L, 0);
                int second = Next(5, 123L, 0);
                int third = Next(5, 123L, 0);
                Check(first == 0 && second == 1 && third == 2, $"每次 ping 的计数递增（{first}/{second}/{third}）");

                Check(Next(5, 123L, 1) == 0, "升档后计数从 0 重来 → 两端借此重新对齐");
                Check(Next(6, 123L, 0) == 0, "换回合后计数清零 → 两端借此重新对齐");
                Check(Next(5, 999L, 0) == 0, "不同玩家各数各的（互不干扰）");
                varietyReset.Invoke(null, null);
            }

            // 连按不重复：给出「上一句」时不能再抽到同一句（池子 ≥2 句的情况）
            MethodInfo? pickVaried = scenePingType is null ? null : AccessTools.Method(scenePingType, "PickVaried");
            if (pickVaried is null)
            {
                Check(false, "找不到 ScenePing.PickVaried");
            }
            else
            {
                int repeats = 0;
                for (int seed = 0; seed < 30; seed++)
                {
                    string? firstLine = pickVaried.Invoke(null, new object?[] { "IRONCLAD", seed, null, Array.Empty<string>() }) as string;
                    if (firstLine is null) continue;
                    string? secondLine = pickVaried.Invoke(null, new object?[] { "IRONCLAD", seed, firstLine, Array.Empty<string>() }) as string;
                    if (string.Equals(secondLine, firstLine, StringComparison.Ordinal)) repeats++;
                }
                Check(repeats == 0, $"连按不会抽到刚说过的那句（30 组里重复 {repeats} 次）");
            }

            // 原版标签是 [center][fly_in offset_x=±60 offset_y=40]原话[/fly_in][/center]，
            // 换台词时必须把包装留着（否则气泡动画会变）
            const string wrapped = "[center][fly_in offset_x=-60 offset_y=40]快点。[/fly_in][/center]";
            string? rewritten = rewriteMethod.Invoke(null, new object?[] { wrapped, "快点。", "还没好吗。" }) as string;
            Check(rewritten == "[center][fly_in offset_x=-60 offset_y=40]还没好吗。[/fly_in][/center]",
                $"催促台词替换保留游戏的 BBCode 包装 -> {rewritten}");
        }

        // 确定性抽取本身：同种子同结果、不同种子有变化、档位池一定生效
        MethodInfo? pickDet = bank?.GetMethod("PickDeterministic", BindingFlags.NonPublic | BindingFlags.Static);
        if (pickDet is null)
        {
            Check(false, "找不到 LineBank.PickDeterministic");
        }
        else
        {
            try
            {
                string? Deterministic(string scene, string character, int seed, string[] conditions)
                {
                    object?[] callArgs = { scene, character, seed, null, conditions };
                    return pickDet.Invoke(null, callArgs) as string;
                }

                string? first = Deterministic("ping", "IRONCLAD", 424242, Array.Empty<string>());
                bool stable = true;
                for (int i = 0; i < 50; i++)
                {
                    if (Deterministic("ping", "IRONCLAD", 424242, Array.Empty<string>()) != first) stable = false;
                }
                Check(stable && !string.IsNullOrWhiteSpace(first), $"同种子 50 次结果一致（两台机器看到同一句）：{first}");

                var variants = new HashSet<string>();
                for (int seed = 0; seed < 60; seed++)
                {
                    if (Deterministic("ping", "IRONCLAD", seed, Array.Empty<string>()) is string line) variants.Add(line);
                }
                Check(variants.Count >= 2, $"不同回合/玩家会抽到不同催促语（60 个种子出现 {variants.Count} 种）");

                // 档位池：带 wait_angry 条件时绝不能落回 normal 池
                HashSet<string> normalPool = Sample(pick, "ping", "IRONCLAD", 400);
                var madLines = new HashSet<string>();
                for (int seed = 0; seed < 30; seed++)
                {
                    string? line = Deterministic("ping", "IRONCLAD", seed, new[] { "wait_angry", "wait_urgent" });
                    if (line is not null) madLines.Add(line);
                }
                Check(madLines.Count > 0 && !madLines.Any(normalPool.Contains),
                    $"更凶档说的是 wait_angry 池（{madLines.Count} 句，且不混入平时池）");

                string? urgent = null;
                for (int seed = 0; seed < 30 && urgent is null; seed++)
                {
                    urgent = Deterministic("ping", "IRONCLAD", seed, new[] { "wait_urgent" });
                }
                Check(urgent is not null && !normalPool.Contains(urgent), $"更急档说的是 wait_urgent 池：{urgent}");
                // ping 故意没有 DEFAULT 通用池：没写专属池的角色必须原样返回 null，
                // 这样 mod 角色（观者、蕾忍…）自带的催促语才不会被我们盖掉；火堆那边仍然有通用池（对照）
                string? moddedPing = Deterministic("ping", "SOME_MODDED_CHARACTER", 7, Array.Empty<string>());
                string? moddedRest = Deterministic("rest_site", "SOME_MODDED_CHARACTER", 7, Array.Empty<string>());
                Check(moddedPing is null, $"ping 没有通用池 → 没写专属池的角色保持游戏原句（拿到 {moddedPing ?? "null"}）");
                Check(!string.IsNullOrWhiteSpace(moddedRest), $"火堆通用池保留 → 没写专属池的角色仍能抽到 DEFAULT：{moddedRest}");

                // 静默猎手：三档都是「……」，情绪只能靠 BBCode 效果区分
                // （正常 无效果 / 更急 [wave] / 更凶 [shake]）
                string? silentNormal = Deterministic("ping", "SILENT", 1, Array.Empty<string>());
                string? silentUrgent = Deterministic("ping", "SILENT", 1, new[] { "wait_urgent" });
                string? silentAngry = Deterministic("ping", "SILENT", 1, new[] { "wait_angry", "wait_urgent" });
                Check(silentNormal == "……", $"静默猎手正常档 = 纯沉默：{silentNormal}");
                Check(silentUrgent == "[wave]……[/wave]", $"静默猎手更急档 = [wave] 晃动：{silentUrgent}");
                Check(silentAngry == "[shake]……[/shake]", $"静默猎手更凶档 = [shake] 发抖：{silentAngry}");

                // 气泡是「角色说出口的话」：ping 的所有池子都不许出现旁白括号（火堆才用那套）；
                // 也不许单指称呼「你」—— 多人局是 2~4 人，催促是对队伍说的
                bool HasSingularYou(string text)
                {
                    int i = text.IndexOf('你');
                    while (i >= 0)
                    {
                        bool allowed = i + 1 < text.Length && (text[i + 1] == '们' || text[i + 1] == '好');
                        if (!allowed) return true;
                        i = text.IndexOf('你', i + 1);
                    }
                    return false;
                }

                var pingParens = new List<string>();
                var pingSingularYou = new List<string>();
                foreach (string character in new[] { "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER", "DEFAULT" })
                {
                    foreach (string[] pools in new[]
                             {
                                 Array.Empty<string>(), new[] { "wait_urgent" }, new[] { "wait_angry", "wait_urgent" },
                             })
                    {
                        for (int seed = 0; seed < 40; seed++)
                        {
                            string? line = Deterministic("ping", character, seed, pools);
                            if (line is null) continue;
                            if (line.Contains('（') || line.Contains('）')) pingParens.Add(line);
                            if (HasSingularYou(line)) pingSingularYou.Add(line);
                        }
                    }
                }
                Check(pingParens.Count == 0, pingParens.Count == 0
                    ? "ping 台词里没有旁白括号（气泡代表说出口的话）"
                    : $"ping 台词里出现了旁白括号：{string.Join(" / ", pingParens.Distinct())}");
                Check(pingSingularYou.Count == 0, pingSingularYou.Count == 0
                    ? "ping 台词里没有单指称呼「你」（多人局 2~4 人）"
                    : $"ping 台词里出现了单指「你」：{string.Join(" / ", pingSingularYou.Distinct())}");
            }
            catch (TargetInvocationException e)
            {
                Check(false, "PickDeterministic() 抛异常: " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message);
            }
        }

        // _ping 时间设置（lines.json）确实被解析到了
        MethodInfo? pingTiming = bank?.GetMethod("GetPingTiming", BindingFlags.NonPublic | BindingFlags.Static);
        if (pingTiming is null)
        {
            Check(false, "找不到 LineBank.GetPingTiming");
        }
        else
        {
            object? value = pingTiming.Invoke(null, null);
            Type? timingType = value?.GetType();
            int urgent = ReadIntField(value, "UrgentAfterSeconds");
            int angry = ReadIntField(value, "AngryAfterSeconds");
            Check(urgent == 60 && angry == 300, $"lines.json 的 _ping 设置解析正确（更急 {urgent}s / 更凶 {angry}s）");

            // 键名写错的话上面那条会被兜底值糊过去，所以再用一个不同的数值直接过一遍解析器
            MethodInfo? parseForPing = bank?.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static);
            const string pingJson = "{ \"_ping\": { \"urgentAfterSeconds\": 7, \"angryAfterSeconds\": 9 }, \"s\": { \"IRONCLAD\": [\"x\"] } }";
            object? parsed = parseForPing?.Invoke(null, new object?[] { pingJson });
            int customUrgent = ReadIntField(parsed, "PingUrgentAfterSeconds");
            int customAngry = ReadIntField(parsed, "PingAngryAfterSeconds");
            Check(customUrgent == 7 && customAngry == 9,
                $"_ping 的键名与数值真的被解析（喂 7/9 得到 {customUrgent}/{customAngry}）");

            // 每次 ping 换一句的开关（_ping.perPingVariety）
            const string varietyJson = "{ \"_ping\": { \"perPingVariety\": false }, \"s\": { \"IRONCLAD\": [\"x\"] } }";
            object? parsedVariety = parseForPing?.Invoke(null, new object?[] { varietyJson });
            bool? varietyParsed = ReadBoolField(parsedVariety, "PingPerPingVariety");
            Check(varietyParsed == false, $"lines.json 的 _ping.perPingVariety 解析正确（喂 false 读到 {varietyParsed}）");

            MethodInfo? varietyGetter = bank?.GetMethod("GetPingVariety", BindingFlags.NonPublic | BindingFlags.Static);
            bool currentVariety = varietyGetter?.Invoke(null, null) is bool enabled && enabled;
            Check(currentVariety, "当前生效的 lines.json 默认是「每次 ping 换一句」");

            // 调试预览键：解析、默认关闭、按键名能被 Godot 认出来
            const string debugJson = "{ \"_debug\": { \"pingPreviewKey\": \"F8\" }, \"s\": { \"IRONCLAD\": [\"x\"] } }";
            object? parsedDebug = parseForPing?.Invoke(null, new object?[] { debugJson });
            string? previewKey = ReadStringField(parsedDebug, "PingPreviewKey");
            Check(previewKey == "F8", $"lines.json 的 _debug.pingPreviewKey 解析正确（读到 \"{previewKey}\"）");

            MethodInfo? previewKeyGetter = bank?.GetMethod("GetPingPreviewKey", BindingFlags.NonPublic | BindingFlags.Static);
            string? currentKey = previewKeyGetter?.Invoke(null, null) as string;
            Check(string.IsNullOrEmpty(currentKey), $"当前生效的 lines.json 预览键是关闭的（\"{currentKey}\"）");

            // 预览键的名字必须能被 Godot 的 Key 枚举认出来。
            // 注意：这里用反射查（不能在预检里直接写 Godot.Key —— 那会让 Preflight.Main 在
            // 解析器注册之前就去解 GodotSharp，直接 FileNotFound）。
            Type? keyType = Type.GetType("Godot.Key, GodotSharp");
            bool keyParses = false;
            if (keyType is not null)
            {
                try
                {
                    object? parsedKey = Enum.Parse(keyType, "F8", ignoreCase: true);
                    keyParses = parsedKey is not null && Convert.ToInt32(parsedKey) != 0;
                }
                catch
                {
                    keyParses = false;
                }
            }
            Check(keyParses, "Godot 按键名可解析（F8 → Key.F8）—— 预览键不会因为名字写错而静默失效");
        }

        // 遗物类型 → 氛围条件（纯函数）：含真假茶具互斥这条核心规则
        Type? conditionsType = mod.GetType("MoreCharacterLines.RestSiteConditions");
        MethodInfo? flavorConditions = conditionsType?.GetMethod("FlavorConditions",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (flavorConditions is null)
        {
            Check(false, "找不到 RestSiteConditions.FlavorConditions");
        }
        else
        {
            List<string> Of(params string[] relicClassNames) =>
                (List<string>)flavorConditions.Invoke(null, new object?[]
                {
                    relicClassNames.Select(n => Type.GetType($"MegaCrit.Sts2.Core.Models.Relics.{n}, sts2")).ToArray(),
                })!;

            List<string> shovel = Of("Shovel");
            Check(shovel.Count == 1 && shovel[0] == "relic_shovel", "铲子 → relic_shovel");

            Check(Of("StoneHumidifier").Contains("relic_humidifier"), "石炉加湿器 → relic_humidifier");

            List<string> realTea = Of("VenerableTeaSet");
            Check(realTea.Contains("relic_tea_set") && !realTea.Contains("relic_fake_tea_set"),
                "只有真茶具 → 只说真货台词");

            List<string> fakeTea = Of("FakeVenerableTeaSet");
            Check(fakeTea.Contains("relic_fake_tea_set") && !fakeTea.Contains("relic_tea_set"),
                "只有假茶具 → 说假货台词");

            List<string> bothTea = Of("VenerableTeaSet", "FakeVenerableTeaSet");
            Check(bothTea.Contains("relic_tea_set") && !bothTea.Contains("relic_fake_tea_set"),
                "真假茶具同时存在 → 只留真货台词（假货台词不出现）");

            int allCount = Of("Shovel", "MeatCleaver", "MiniatureTent", "DreamCatcher", "TinyMailbox",
                "RegalPillow", "PaelsGrowth", "StoneHumidifier", "VenerableTeaSet").Count;
            Check(allCount == 9, $"9 种遗物各产生 1 个氛围条件（实际 {allCount}）");
        }

        // 解析器：新写法（条件挂在角色下面）和旧写法（_conditions 大栏）都要认
        MethodInfo? parse = bank?.GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static);
        if (parse is null)
        {
            Check(false, "找不到 LineBank.Parse");
        }
        else
        {
            const string newLayout =
                "{ \"s\": { \"IRONCLAD\": { \"normal\": [\"n1\"], \"low_hp\": [\"l1\",\"l2\"] } } }";
            const string oldLayout =
                "{ \"s\": { \"IRONCLAD\": [\"n1\"], \"_conditions\": { \"low_hp\": { \"_chance\": 0.5, \"IRONCLAD\": [\"l1\",\"l2\"] } } } }";

            try
            {
                Check(PoolContains(parse, newLayout, "s", "IRONCLAD", "normal", "n1")
                      && PoolContains(parse, newLayout, "s", "IRONCLAD", "low_hp", "l2"),
                    "新写法（条件挂在角色下面）解析正确");

                Check(PoolContains(parse, oldLayout, "s", "IRONCLAD", "normal", "n1")
                      && PoolContains(parse, oldLayout, "s", "IRONCLAD", "low_hp", "l2"),
                    "旧写法（_conditions 大栏）仍然兼容");
            }
            catch (TargetInvocationException e)
            {
                Check(false, "Parse() 抛异常: " + e.InnerException?.GetType().Name + ": " + e.InnerException?.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "预检通过 ✅" : $"预检失败 ❌（{_failures} 项）");
        return _failures == 0 ? 0 : 1;
    }

    /// <summary>借用 LineBank.Parse 解析一段 JSON，检查「场景/角色/池」里有没有某句台词。</summary>
    private static bool PoolContains(MethodInfo parse, string json, string scene, string characterId, string pool, string line)
    {
        object? file = parse.Invoke(null, new object?[] { json });
        if (file is null) { Console.WriteLine("       [debug] Parse 返回 null"); return false; }

        if (GetField<System.Collections.IDictionary>(file, "Scenes") is not { } scenes)
        {
            Console.WriteLine($"       [debug] 取不到 Scenes 字段；实际字段：{string.Join(",", file.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(f => f.Name))}");
            return false;
        }

        if (!scenes.Contains(scene)) { Console.WriteLine("       [debug] 找不到场景 " + scene); return false; }

        if (GetField<System.Collections.IDictionary>(scenes[scene]!, "Characters") is not { } characters)
        {
            Console.WriteLine("       [debug] 取不到 Characters 字段");
            return false;
        }

        if (!characters.Contains(characterId)) { Console.WriteLine("       [debug] 场景里没有角色 " + characterId); return false; }

        if (characters[characterId] is not System.Collections.IDictionary pools) { Console.WriteLine("       [debug] 角色值不是字典"); return false; }
        if (!pools.Contains(pool)) { Console.WriteLine($"       [debug] 角色里没有池 {pool}；实际池：{string.Join(",", pools.Keys.Cast<object>())}"); return false; }

        return pools[pool] is List<string> lines && lines.Contains(line);
    }

    private static T? GetField<T>(object instance, string name) where T : class
    {
        return instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(instance) as T;
    }

    private static HashSet<string> Sample(MethodInfo pick, string scene, string character, int count)
    {
        return Sample(pick, scene, character, Array.Empty<string>(), count);
    }

    private static HashSet<string> Sample(MethodInfo pick, string scene, string character, string[] conditions, int count)
    {
        var seen = new HashSet<string>();
        for (int i = 0; i < count; i++)
        {
            if (pick.Invoke(null, new object?[] { scene, character, conditions }) is string line) seen.Add(line);
        }
        return seen;
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "[ok]   " : "[FAIL] ") + what);
        if (!ok) _failures++;
    }

    /// <summary>某个方法上挂上补丁了吗（用来确认 ping 的挂点没被游戏更新打掉）。</summary>
    private static void CheckPatched(string typeName, string methodName)
    {
        Type? type = Type.GetType(typeName);
        MethodInfo? method = type?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null)
        {
            Check(false, $"找不到 {typeName.Split(',')[0]}.{methodName}");
            return;
        }

        Patches? info = Harmony.GetPatchInfo(method);
        int count = (info?.Postfixes?.Count ?? 0) + (info?.Prefixes?.Count ?? 0);
        Check(count > 0, $"{typeName.Split(',')[0]}.{methodName} 已挂上补丁（{count} 个）");
    }

    /// <summary>游戏里的成员（属性 / 字段）还在不在 —— 补丁要用的目标先在这里确认。</summary>
    private static void CheckMember(string typeName, string memberName)
    {
        Type? type = Type.GetType(typeName);
        if (type is null)
        {
            Check(false, $"找不到类型 {typeName.Split(',')[0]}");
            return;
        }

        bool exists = type
            .GetMember(memberName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Length > 0;
        Check(exists, $"{typeName.Split(',')[0]}.{memberName} 还在");
    }

    /// <summary>读一个 internal 的 int 字段（mod 里的设置项都不是 public）。</summary>
    private static int ReadIntField(object? instance, string name)
    {
        if (instance is null) return -1;
        FieldInfo? field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(instance) is int value ? value : -1;
    }

    /// <summary>读一个 internal 的 string 字段。</summary>
    private static string? ReadStringField(object? instance, string name)
    {
        if (instance is null) return null;
        FieldInfo? field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(instance) as string;
    }

    /// <summary>读一个 internal 的 bool 字段。</summary>
    private static bool? ReadBoolField(object? instance, string name)
    {
        if (instance is null) return null;
        FieldInfo? field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(instance) is bool value ? value : null;
    }
}
