using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace MoreCharacterLines;

/// <summary>
/// 催促语气的时间追踪：什么时候算「等得有点久了」。
///
/// 规则（玩家定）：≥50% 的玩家已经结束回合、剩下的玩家还在继续打时开始计时，
/// 超过 1 分钟 → 更急（wait_urgent），超过 5 分钟 → 更凶（wait_angry），下一回合清零。
///
/// 实现要点：
///   1. **不订阅事件**：只挂在游戏自己的 SetReadyToEndTurn / UndoReadyToEndTurn 后面。
///      这两个方法是确定性模拟的一部分 —— 每个客户端、每个玩家结束回合时都会跑一次，
///      所以「什么时候到 50%」这件事两端天然一致。
///   2. 时钟用各自进程的 <see cref="Time.GetTicksMsec"/>：两端观察同一个逻辑事件的时刻
///      只差几十毫秒（网络延迟），档位阈值是分钟级的，因此判定结果一致。
///   3. 回合号变了就当没等过 —— 「到下一回合重置」不需要额外挂点。
///   4. 比例判定用游戏自己的 <see cref="CombatManager.IsPlayerReadyToEndTurn"/>
///      （内部带锁），不去碰私有集合，线程安全。
/// </summary>
internal static class PingClock
{
    /// <summary>开始计时的时刻（毫秒，0 = 还没开始等）。</summary>
    private static long _halfwayStampMs;

    /// <summary>这个时间戳属于哪一回合（回合变了就作废）。</summary>
    private static int _stampRound = -1;

    private static bool _warned;

    // ── 给补丁调用 ──────────────────────────────────────────────────────────

    /// <summary>有人结束了回合：如果这一下让「已结束回合」达到 50%，开始计时。</summary>
    internal static void NotePlayerEndedTurn(Player? player)
    {
        try
        {
            int round = RoundOf(player);
            if (round < 0) return;

            if (_stampRound != round) ResetTo(round);
            if (_halfwayStampMs != 0) return;                 // 已经在计时了
            if (!TryCountReady(player!, out int ready, out int total)) return;
            if (!IsHalfway(ready, total)) return;

            _halfwayStampMs = Now();
        }
        catch (Exception e)
        {
            WarnOnce(e);
        }
    }

    /// <summary>有人撤销了结束回合：如果不到 50% 了，把计时清掉。</summary>
    internal static void Recheck(Player? player)
    {
        try
        {
            int round = RoundOf(player);
            if (round < 0) return;

            if (_stampRound != round)
            {
                ResetTo(round);
                return;
            }

            if (_halfwayStampMs == 0) return;
            if (!TryCountReady(player!, out int ready, out int total)) return;
            if (!IsHalfway(ready, total)) _halfwayStampMs = 0;
        }
        catch (Exception e)
        {
            WarnOnce(e);
        }
    }

    // ── 给 ScenePing 调用 ───────────────────────────────────────────────────

    /// <summary>现在该用哪一档语气：0 正常 / 1 更急 / 2 更凶。</summary>
    internal static int CurrentTier(Player? player, PingTiming timing)
    {
        try
        {
            int round = RoundOf(player);
            if (round < 0) return PingTone.Normal;

            if (_stampRound != round)
            {
                ResetTo(round);                                // 新回合 → 重新等
                return PingTone.Normal;
            }

            if (_halfwayStampMs == 0) return PingTone.Normal;  // 还没到 50%，不催
            if (!TryCountReady(player!, out int ready, out int total)) return PingTone.Normal;

            double waitedSeconds = (Now() - _halfwayStampMs) / 1000.0;
            return PingTone.Tier(ready, total, waitedSeconds, timing.UrgentAfterSeconds, timing.AngryAfterSeconds);
        }
        catch (Exception e)
        {
            WarnOnce(e);
            return PingTone.Normal;
        }
    }

    /// <summary>预检用：清掉计时状态。</summary>
    internal static void Reset()
    {
        _halfwayStampMs = 0;
        _stampRound = -1;
    }

    /// <summary>预检用：直接问「这算不算到 50% 了」。</summary>
    internal static bool IsHalfway(int readyPlayers, int totalPlayers)
    {
        if (totalPlayers <= 0) return false;
        if (readyPlayers <= 0) return false;
        return readyPlayers >= totalPlayers * PingTone.HalfwayRatio;
    }

    /// <summary>预检用：看一眼当前记着的等待秒数（没在等返回 -1）。</summary>
    internal static double WaitedSeconds()
    {
        if (_halfwayStampMs == 0) return -1.0;
        return (Now() - _halfwayStampMs) / 1000.0;
    }

    // ── 内部 ────────────────────────────────────────────────────────────────

    private static long Now() => (long)Time.GetTicksMsec();

    private static void ResetTo(int round)
    {
        _halfwayStampMs = 0;
        _stampRound = round;
    }

    /// <summary>当前回合号；拿不到返回 -1。</summary>
    private static int RoundOf(Player? player)
    {
        try
        {
            CombatState? state = CombatStateOf(player);
            return state?.RoundNumber ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>玩家所在的战斗状态（turn-based 模拟里的那份）。</summary>
    private static CombatState? CombatStateOf(Player? player)
    {
        if (player is null) return null;

        IRunState? runState = player.RunState;
        return (runState?.CurrentRoom as CombatRoom)?.CombatState;
    }

    /// <summary>数一下「已结束回合 / 参战玩家」——比例判定只信游戏自己的接口。</summary>
    private static bool TryCountReady(Player player, out int ready, out int total)
    {
        ready = 0;
        total = 0;

        CombatState? state = CombatStateOf(player);
        CombatManager? manager = CombatManager.Instance;
        if (state is null || manager is null) return false;

        foreach (Player p in state.Players)
        {
            total++;
            if (manager.IsPlayerReadyToEndTurn(p)) ready++;
        }

        return total > 0;
    }

    private static void WarnOnce(Exception e)
    {
        if (_warned) return;
        _warned = true;
        Log.Warn("[MoreCharacterLines] 催促计时失败（不影响游戏，语气按正常档走）：" + e.Message);
    }
}

/// <summary>
/// 「有人结束回合」——挂在这个方法后面记时间戳。
/// 这个方法每个客户端都会跑（结束回合是确定性模拟的一部分），所以两端计时起点一致。
/// </summary>
[HarmonyPatch(typeof(CombatManager), "SetReadyToEndTurn")]
internal static class PingClockEndedTurnPatch
{
    private static void Postfix(Player player)
    {
        PingClock.NotePlayerEndedTurn(player);
    }
}

/// <summary>「有人撤销结束回合」——不到 50% 就取消计时。</summary>
[HarmonyPatch(typeof(CombatManager), "UndoReadyToEndTurn")]
internal static class PingClockUnendedTurnPatch
{
    private static void Postfix(Player player)
    {
        PingClock.Recheck(player);
    }
}
