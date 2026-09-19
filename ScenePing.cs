using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace MoreCharacterLines;

/// <summary>
/// 多人催促（ping）场景 —— 结束回合后按下的那个 Ping 按钮。
///
/// 游戏原本的链路（sts2.dll）：
///     NPingButton.OnRelease
///       → FlavorSynchronizer.SendEndTurnPing()          // 限流 1 秒 + 发 EndTurnPingMessage
///            → CreateEndTurnPingDialogueIfNecessary(自己) // 本地气泡
///     对端：HandleEndTurnPingMessage(senderId)
///            → CreateEndTurnPingDialogueIfNecessary(发送者)
/// 也就是说，**本地和远端两条路都汇到同一个方法**，所以只需要在这一个方法后面动手。
///
/// 原版行为：key = "&lt;角色&gt;.banter.&lt;alive|dead&gt;.endTurnPing"，
/// 在角色头顶弹一个 1.5 秒的对话气泡（角色专属颜色）。
///
/// 本 mod 的做法（Postfix）：原版照常跑完，然后把气泡里的文本换掉 ——
///
///   1. **死人保持原版**（原版就是「……」，玩家要求死人不需要说话）。
///   2. **两端一致**：台词用确定性抽取（<see cref="LineBank.PickDeterministic"/>），
///      种子来自两端都相同的同步状态 ——
///         回合号 + 催促者 NetId + 语气档位（PingClock / PingTone）
///      注意不能用「这是本回合第几次 ping」这种计数器：EndTurnPingMessage 走的是
///      不可靠通道（Mode=1，和心跳同级；玩法消息走 Mode=2），丢包就会让两端错位。
///   3. **不碰网络协议**：不发任何自定义消息，所以对端没装 mod 也能正常联机 ——
///      他那边照常显示游戏原话，我们这边显示 mod 台词。
///   4. 语气随时间分档：等够 1 分钟 → 更急，5 分钟 → 更凶（见 PingClock）。
/// </summary>
[HarmonyPatch(typeof(FlavorSynchronizer), "CreateEndTurnPingDialogueIfNecessary")]
internal static class ScenePing
{
    /// <summary>私有字典：玩家 → 他当前的气泡（原版用它实现「再 ping 就换掉上一条」）。</summary>
    private static AccessTools.FieldRef<FlavorSynchronizer, Dictionary<Player, NSpeechBubbleVfx>>? _dialoguesRef;

    /// <summary>气泡里的原始文本（原版写进去的那句，标签里还套着 BBCode 包装）。</summary>
    private static FieldInfo? _bubbleTextField;

    /// <summary>气泡实际显示的富文本标签。</summary>
    private static FieldInfo? _bubbleLabelField;

    private static void Postfix(FlavorSynchronizer __instance, Player player)
    {
        try
        {
            if (player is null) return;

            // 死人保持原版「……」
            if (player.Creature is null || player.Creature.IsDead) return;

            string characterId = PlayerContext.CharacterId(player);
            if (string.IsNullOrEmpty(characterId)) return;

            PingTiming timing = LineBank.GetPingTiming();
            int tier = PingClock.CurrentTier(player, timing);
            int round = RoundOf(player);

            // 「第几次 ping」默认算进种子里（每次按都会换一句）；关掉就退回"同回合同档位永远同一句"
            bool variety = LineBank.GetPingVariety();
            string varietyKey = PingVariety.Key(round, player.NetId, tier);
            int pingIndex = variety ? PingVariety.Next(round, player.NetId, tier) : 0;
            int seed = Seed(round, player.NetId, tier, pingIndex);
            string[] pools = PingTone.Pools(tier);

            // 开了"每次换一句"时，还要避开上一次刚说过的那句 ——
            // 池子只有 2~3 句，纯随机会有 30%~50% 概率连按两次抽到同一句，看起来就像"没变"
            string? text = variety
                ? PickVaried(characterId, seed, PingVariety.LastLine(varietyKey), pools)
                : LineBank.PickDeterministic(Scenes.Ping, characterId, seed, out _, pools);

            if (string.IsNullOrEmpty(text)) return;   // 我们没写词 → 保持原版
            if (variety) PingVariety.Remember(varietyKey, text!);

            NSpeechBubbleVfx? bubble = GetDialogue(__instance, player);
            if (bubble is null) return;

            MegaRichTextLabel? label = GetLabel(bubble);
            if (label is null) return;

            // 原版标签文本是 [center][fly_in offset_x=±60 offset_y=40]原话[/fly_in][/center]，
            // 这里只把中间那句换掉，包装（也就是入场动画）保持游戏自己的写法。
            label.Text = Rewrite(label.Text, GetOriginalText(bubble), text!);

            Log.Info($"[MoreCharacterLines] 催促台词（档位 {tier}）：{text}");
        }
        catch (Exception e)
        {
            // 出错就什么都不做：气泡里还是游戏原话，不崩、不刷屏
            Log.Warn("[MoreCharacterLines] 设置催促台词失败：" + e.Message);
        }
    }

    // ── 取气泡与文本 ────────────────────────────────────────────────────────

    private static NSpeechBubbleVfx? GetDialogue(FlavorSynchronizer sync, Player player)
    {
        try
        {
            _dialoguesRef ??= AccessTools.FieldRefAccess<FlavorSynchronizer, Dictionary<Player, NSpeechBubbleVfx>>("_endTurnPingDialogues");
            Dictionary<Player, NSpeechBubbleVfx>? dialogues = _dialoguesRef(sync);
            if (dialogues is null) return null;
            return dialogues.TryGetValue(player, out NSpeechBubbleVfx? bubble) ? bubble : null;
        }
        catch
        {
            return null;
        }
    }

    private static MegaRichTextLabel? GetLabel(NSpeechBubbleVfx bubble)
    {
        _bubbleLabelField ??= AccessTools.Field(typeof(NSpeechBubbleVfx), "_label");
        return _bubbleLabelField?.GetValue(bubble) as MegaRichTextLabel;
    }

    private static string? GetOriginalText(NSpeechBubbleVfx bubble)
    {
        _bubbleTextField ??= AccessTools.Field(typeof(NSpeechBubbleVfx), "_text");
        return _bubbleTextField?.GetValue(bubble) as string;
    }

    /// <summary>把标签文本里的原话换成我们的台词，保留游戏自己套的 BBCode 包装。</summary>
    internal static string Rewrite(string? labelText, string? original, string replacement)
    {
        const string fallbackPrefix = "[center]";
        const string fallbackSuffix = "[/center]";

        if (string.IsNullOrEmpty(labelText) || string.IsNullOrEmpty(original)) return fallbackPrefix + replacement + fallbackSuffix;

        int index = labelText.IndexOf(original, StringComparison.Ordinal);
        if (index < 0) return fallbackPrefix + replacement + fallbackSuffix;

        return labelText.Substring(0, index) + replacement + labelText.Substring(index + original.Length);
    }

    // ── 种子与"第几次" ─────────────────────────────────────────────────────

    /// <summary>
    /// 抽一句，并尽量避开上一次刚说过的那句（池子 ≥2 句时"按一下换一句"才真的成立）。
    /// 两端拿到的 <paramref name="last"/> 与 <paramref name="seed"/> 相同 ⇒ 抽到的结果也相同。
    /// </summary>
    internal static string? PickVaried(string characterId, int seed, string? last, string[] pools)
    {
        string? text = LineBank.PickDeterministic(Scenes.Ping, characterId, seed, out _, pools);
        if (text is null) return null;

        for (int attempt = 1; attempt <= 6 && string.Equals(text, last, StringComparison.Ordinal); attempt++)
        {
            string? retry = LineBank.PickDeterministic(Scenes.Ping, characterId, seed + attempt * 7919, out _, pools);
            if (retry is not null) text = retry;
        }

        return text;
    }

    /// <summary>
    /// 种子 = 回合号 + 催促者 NetId + 语气档位 +（可选）这一档里第几次 ping。
    ///
    /// 前三个在两端完全相同 ⇒ 抽出来的台词相同；第四个（<paramref name="pingIndex"/>）也相同 ——
    /// 前提是两端的 ping 消息一个都没丢。丢包时计数会错开一位，台词就会不一样，
    /// 但**升档或换回合时会从 0 重新对齐**（见 PingVariety）。
    /// 把 lines.json 的 `_ping.perPingVariety` 设成 false 就完全不算这一项，退回"永远同一句"。
    /// </summary>
    internal static int Seed(int round, object? netId, int tier, int pingIndex = 0)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + round;
            hash = hash * 31 + tier;
            hash = hash * 31 + pingIndex;

            string id = Convert.ToString(netId, CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (char c in id) hash = hash * 31 + c;

            return hash;
        }
    }

    private static int RoundOf(Player player)
    {
        try
        {
            IRunState? runState = player.RunState;
            return (runState?.CurrentRoom as CombatRoom)?.CombatState?.RoundNumber ?? -1;
        }
        catch
        {
            return -1;
        }
    }
}

/// <summary>
/// 「这是本回合本档位第几次 ping」—— 让每次按下 Ping 都换一句。
///
/// 两端各自数自己渲染过的 ping：消息不丢的话两个客户端的计数完全相同，抽出来的台词也一样。
/// 丢包时会错开一位（那一句之后两边会不一样），但**升档（键里含 tier）或进入下一回合
/// （整表清空）时会从 0 重新对齐** —— 漂移不会永久累积。
///
/// 不想要这个行为：把 lines.json 的 `_ping.perPingVariety` 设成 false（退回"永远同一句"）。
/// </summary>
internal static class PingVariety
{
    private static readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> LastLines = new(StringComparer.Ordinal);
    private static int _lastRound = int.MinValue;

    /// <summary>「这一局里谁、哪回合、哪一档」—— 计数与"上一条"都用这个键。</summary>
    internal static string Key(int round, object? netId, int tier)
    {
        return round + "|" + Convert.ToString(netId, CultureInfo.InvariantCulture) + "|" + tier;
    }

    internal static int Next(int round, object? netId, int tier)
    {
        if (round != _lastRound)
        {
            Counts.Clear();          // 换回合：两端一起从 0 开始
            LastLines.Clear();
            _lastRound = round;
        }

        string key = Key(round, netId, tier);
        Counts.TryGetValue(key, out int index);
        Counts[key] = index + 1;
        return index;
    }

    /// <summary>上一次在这个键下说过的那句（没有就 null）—— 用来避免连按两次一模一样。</summary>
    internal static string? LastLine(string key)
    {
        return LastLines.TryGetValue(key, out string? line) ? line : null;
    }

    internal static void Remember(string key, string line)
    {
        LastLines[key] = line;
    }

    /// <summary>预检用：清空计数。</summary>
    internal static void Reset()
    {
        Counts.Clear();
        LastLines.Clear();
        _lastRound = int.MinValue;
    }
}
