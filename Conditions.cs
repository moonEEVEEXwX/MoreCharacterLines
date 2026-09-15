using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace MoreCharacterLines;

/// <summary>条件名 —— 必须和 lines.json 里角色下面写的池名一致。</summary>
internal static class Conditions
{
    /// <summary>血量 ≤ 25%（游戏自己的低血判定，与低血边框动画同源）。</summary>
    internal const string LowHp = "low_hp";

    /// <summary>南瓜蜡烛剩余层数 ≤ 2 —— 该添火了。</summary>
    internal const string CandleLow = "candle_low";

    /// <summary>壶铃已经练满 3 层 —— 播报一次“看着肌肉很满意”，之后不再播。</summary>
    internal const string GiryaMaxed = "girya_maxed";

    /// <summary>壶铃还没练满 —— 鼓励去锻炼。</summary>
    internal const string GiryaProgress = "girya_progress";

    /// <summary>ping：已经等了有一会儿（默认 1 分钟）→ 催促语气更急。</summary>
    internal const string WaitUrgent = "wait_urgent";

    /// <summary>ping：等太久了（默认 5 分钟）→ 催促语气更凶。</summary>
    internal const string WaitAngry = "wait_angry";

    internal const string Shovel = "relic_shovel";                  // 铲子（挖掘）
    internal const string Cleaver = "relic_cleaver";                // 切肉刀（烹饪）
    internal const string Tent = "relic_tent";                      // 微型帐篷（可多选）
    internal const string DreamCatcher = "relic_dream_catcher";     // 捕梦网
    internal const string Mailbox = "relic_mailbox";                // 小邮箱
    internal const string Pillow = "relic_pillow";                  // 皇家枕头
    internal const string PaelsGrowth = "relic_paels_growth";       // 佩尔的增生组织（克隆）
    internal const string Humidifier = "relic_humidifier";          // 石炉加湿器（休息 +5 最大生命）
    internal const string TeaSet = "relic_tea_set";                 // 古茶具套装（下场战斗 +2 费）
    internal const string FakeTeaSet = "relic_fake_tea_set";        // 古茶具套装？？？（假货，+1 费）
}

/// <summary>
/// 火堆场景用到的条件（按优先级从高到低）。
///
/// 顺序说明：
///   1. candle_low    —— 可以立刻操作的提示（该添火了），最重要
///   2. girya_maxed   —— 壶铃练满的一次性纪念播报；如果这轮已经播了更重要的，
///                       它不会被消耗掉，下次火堆继续尝试（即“推迟播报”）
///   3. low_hp        —— 血量低时说的话
///   4. 各遗物氛围台词 —— 各自按 lines.json 里的 _conditionChance 小概率出现
///   5. girya_progress —— 还没练满时的鼓励
/// </summary>
internal static class RestSiteConditions
{
    /// <summary>南瓜蜡烛剩多少层时算“该添火了”。</summary>
    private const int CandleLowStacks = 2;

    /// <summary>壶铃最大锻炼次数的兜底值（正常从游戏里的 Girya.maxLifts 读）。</summary>
    private const int GiryaMaxLiftsFallback = 3;

    private static int? _giryaMaxLifts;

    /// <summary>壶铃练满这一条播报的“一次性”键（按 种子 + 角色 区分，避免不同存档串味）。</summary>
    internal static string GiryaAnnouncedKey(Player? player, IRunState? runState)
    {
        string seed;
        try
        {
            seed = runState?.Rng?.StringSeed ?? "unknown";
        }
        catch
        {
            seed = "unknown";
        }

        return $"girya_maxed:{seed}:{PlayerContext.CharacterId(player)}";
    }

    internal static string[] For(Player? player, IRunState? runState)
    {
        var conditions = new List<string>();

        try
        {
            // 1) 南瓜蜡烛快没火了
            PumpkinCandle? candle = PlayerContext.FindRelic<PumpkinCandle>(player);
            if (candle is not null && candle.KindleCount <= CandleLowStacks)
            {
                conditions.Add(Conditions.CandleLow);
            }

            // 2) 壶铃练满的一次性播报
            Girya? girya = PlayerContext.FindRelic<Girya>(player);
            if (girya is not null)
            {
                int maxLifts = GiryaMaxLifts();
                if (girya.TimesLifted >= maxLifts && !OneShot.IsDone(GiryaAnnouncedKey(player, runState)))
                {
                    conditions.Add(Conditions.GiryaMaxed);
                }
            }

            // 3) 血量低
            if (PlayerContext.IsLowHealth(player)) conditions.Add(Conditions.LowHp);

            // 4) 遗物氛围台词（各自小概率）—— 纯逻辑，见 FlavorConditions
            conditions.AddRange(FlavorConditions(OwnedRelicTypes(player)));

            // 5) 壶铃还没练满
            if (girya is not null && girya.TimesLifted < GiryaMaxLifts())
            {
                conditions.Add(Conditions.GiryaProgress);
            }
        }
        catch (Exception e)
        {
            Log.Warn("[MoreCharacterLines] 计算火堆条件失败：" + e.Message);
        }

        return conditions.ToArray();
    }

    /// <summary>
    /// 纯逻辑：根据「身上有哪些遗物类型」决定要说哪些氛围台词。
    /// 用类型而不是遗物 ID 字符串 —— 写错类名会直接编译不过，不会静默失效。
    /// 抽成纯函数是为了能在预检里直接测（尤其是真假茶具的互斥）。
    /// </summary>
    internal static List<string> FlavorConditions(IReadOnlyCollection<Type> ownedRelicTypes)
    {
        var conditions = new List<string>();

        bool Has<T>() where T : RelicModel
        {
            foreach (Type type in ownedRelicTypes)
            {
                if (typeof(T).IsAssignableFrom(type)) return true;
            }
            return false;
        }

        if (Has<Shovel>()) conditions.Add(Conditions.Shovel);
        if (Has<MeatCleaver>()) conditions.Add(Conditions.Cleaver);
        if (Has<MiniatureTent>()) conditions.Add(Conditions.Tent);
        if (Has<DreamCatcher>()) conditions.Add(Conditions.DreamCatcher);
        if (Has<TinyMailbox>()) conditions.Add(Conditions.Mailbox);
        if (Has<RegalPillow>()) conditions.Add(Conditions.Pillow);
        if (Has<PaelsGrowth>()) conditions.Add(Conditions.PaelsGrowth);
        if (Has<StoneHumidifier>()) conditions.Add(Conditions.Humidifier);

        // 真假茶具：有真货时只说真货那几句，假货台词不出现
        bool hasRealTeaSet = Has<VenerableTeaSet>();
        if (hasRealTeaSet) conditions.Add(Conditions.TeaSet);
        if (!hasRealTeaSet && Has<FakeVenerableTeaSet>()) conditions.Add(Conditions.FakeTeaSet);

        return conditions;
    }

    /// <summary>收集玩家身上遗物的具体类型。</summary>
    private static Type[] OwnedRelicTypes(Player? player)
    {
        try
        {
            IReadOnlyList<RelicModel>? relics = player?.Relics;
            if (relics is null) return Array.Empty<Type>();

            var types = new List<Type>(relics.Count);
            foreach (RelicModel relic in relics) types.Add(relic.GetType());
            return types.ToArray();
        }
        catch (Exception e)
        {
            Log.Warn("[MoreCharacterLines] 读取遗物列表失败：" + e.Message);
            return Array.Empty<Type>();
        }
    }

    /// <summary>Girya.maxLifts 是编译期常量，用反射读一次避免版本改动后失配。</summary>
    private static int GiryaMaxLifts()
    {
        if (_giryaMaxLifts is not null) return _giryaMaxLifts.Value;

        try
        {
            FieldInfo? field = typeof(Girya).GetField("maxLifts", BindingFlags.Public | BindingFlags.Static);
            _giryaMaxLifts = field?.GetValue(null) is int value ? value : GiryaMaxLiftsFallback;
        }
        catch
        {
            _giryaMaxLifts = GiryaMaxLiftsFallback;
        }

        return _giryaMaxLifts.Value;
    }
}

/// <summary>
/// 多人催促（ping）的语气分档 —— 纯函数，预检可以直接喂数字测。
///
/// 规则（玩家定）：**≥50% 的玩家已经结束回合**、剩下的玩家还在继续打时开始计时；
///   - 超过 <c>urgentAfterSeconds</c> 秒（默认 60）→ 更急（wait_urgent）
///   - 超过 <c>angryAfterSeconds</c> 秒（默认 300）→ 更凶（wait_angry）
///   - 下一回合（RoundNumber 变化）清零重来
///
/// 注意：档位是**两端各算各的**，算出来必然一样 —— 计时起点来自两端都收到的
/// 同一个「结束回合」事件，秒数阈值判定只差几十毫秒（见 PingClock）。
/// </summary>
internal static class PingTone
{
    /// <summary>多少比例的玩家结束回合后开始计时（玩家要求是 50%，写死）。</summary>
    internal const double HalfwayRatio = 0.5;

    internal const int Normal = 0;
    internal const int Urgent = 1;
    internal const int Angry = 2;

    /// <summary>算出当前语气档位：0 正常 / 1 更急 / 2 更凶。</summary>
    internal static int Tier(int readyPlayers, int totalPlayers, double waitedSeconds,
                             int urgentAfterSeconds, int angryAfterSeconds)
    {
        if (totalPlayers <= 0) return Normal;
        if (readyPlayers <= 0) return Normal;
        if (readyPlayers < totalPlayers * HalfwayRatio) return Normal;   // 还没到 50%，不催
        if (waitedSeconds >= angryAfterSeconds) return Angry;
        if (waitedSeconds >= urgentAfterSeconds) return Urgent;
        return Normal;
    }

    /// <summary>档位 → 条件池（按优先级从高到低；正常档返回空数组 = 用平时池）。</summary>
    internal static string[] Pools(int tier)
    {
        return tier switch
        {
            >= Angry => new[] { Conditions.WaitAngry, Conditions.WaitUrgent },
            Urgent => new[] { Conditions.WaitUrgent },
            _ => Array.Empty<string>(),
        };
    }
}
