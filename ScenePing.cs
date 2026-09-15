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
            int seed = Seed(RoundOf(player), player.NetId, tier);

            string? text = LineBank.PickDeterministic(
                Scenes.Ping, characterId, seed, out _, PingTone.Pools(tier));

            if (string.IsNullOrEmpty(text)) return;   // 我们没写词 → 保持原版

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

    // ── 两端一致的种子 ──────────────────────────────────────────────────────

    /// <summary>
    /// 种子 = 回合号 + 催促者 NetId + 语气档位。
    /// 这三样在两端完全相同，所以抽出来的台词也相同 —— 不需要额外同步。
    /// 同一回合同一档位里重复 ping 会说同一句（像同一个人反复催），换回合/升档才换词。
    /// </summary>
    internal static int Seed(int round, object? netId, int tier)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + round;
            hash = hash * 31 + tier;

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
