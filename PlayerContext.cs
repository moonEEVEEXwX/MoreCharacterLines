using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterLines;

/// <summary>
/// 游戏侧上下文：取当前玩家、读遗物、判断血量等。
/// 各场景补丁共用这里的逻辑。
/// </summary>
internal static class PlayerContext
{
    /// <summary>拿不到角色自带判定时的兜底阈值：25%（游戏默认就是这个值）。</summary>
    private const double LowHpFallbackThreshold = 0.25;

    private static bool _lowHpGetterResolved;
    private static MethodInfo? _lowHpGetter;

    /// <summary>取本地玩家；拿不到就退回 1 号位玩家。</summary>
    internal static Player? FromRunState(IRunState? runState)
    {
        if (runState is null) return null;

        try
        {
            return LocalContext.GetMe(runState) ?? runState.Players?.FirstOrDefault();
        }
        catch (Exception e)
        {
            Log.Warn("[CharacterLines] 取当前玩家失败：" + e.Message);
            return null;
        }
    }

    /// <summary>角色 ID（IRONCLAD / SILENT / ...），取不到返回空串。</summary>
    internal static string CharacterId(Player? player)
    {
        try
        {
            return player?.Character?.Id?.Entry ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>玩家身上有没有某个遗物（按遗物 ID，如 "SHOVEL"）。</summary>
    internal static bool HasRelic(Player? player, string relicId)
    {
        try
        {
            System.Collections.Generic.IReadOnlyList<RelicModel>? relics = player?.Relics;
            if (relics is null) return false;

            foreach (RelicModel relic in relics)
            {
                if (string.Equals(relic.Id.Entry, relicId, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception e)
        {
            Log.Warn("[CharacterLines] 读遗物失败：" + e.Message);
        }

        return false;
    }

    /// <summary>玩家身上有没有某个类型的遗物（要读它的层数时用这个）。</summary>
    internal static T? FindRelic<T>(Player? player) where T : RelicModel
    {
        try
        {
            System.Collections.Generic.IReadOnlyList<RelicModel>? relics = player?.Relics;
            if (relics is null) return null;

            foreach (RelicModel relic in relics)
            {
                if (relic is T typed) return typed;
            }
        }
        catch (Exception e)
        {
            Log.Warn("[CharacterLines] 读遗物失败：" + e.Message);
        }

        return null;
    }

    /// <summary>
    /// 复用游戏自己的低血判定：CharacterModel.IsLowHealth
    /// （protected 的 Func&lt;Creature,bool&gt;，默认 GetHpPercentRemaining() &lt;= 0.25），
    /// 也就是低血边框动画用的那个临界点；拿不到就退回公开的 25% 规则。
    /// </summary>
    internal static bool IsLowHealth(Player? player)
    {
        try
        {
            Creature? creature = player?.Creature;
            if (creature is null) return false;

            Func<Creature, bool>? predicate = GetLowHealthPredicate(player!.Character);
            if (predicate is not null) return predicate(creature);

            return creature.GetHpPercentRemaining() <= LowHpFallbackThreshold;
        }
        catch (Exception e)
        {
            Log.Warn("[CharacterLines] 低血判定失败：" + e.Message);
            return false;
        }
    }

    private static Func<Creature, bool>? GetLowHealthPredicate(CharacterModel? character)
    {
        if (character is null) return null;

        if (!_lowHpGetterResolved)
        {
            _lowHpGetterResolved = true;
            try
            {
                _lowHpGetter = AccessTools.PropertyGetter(typeof(CharacterModel), "IsLowHealth");
            }
            catch
            {
                _lowHpGetter = null;
            }
        }

        if (_lowHpGetter is null) return null;

        try
        {
            return _lowHpGetter.Invoke(character, null) as Func<Creature, bool>;
        }
        catch
        {
            return null;
        }
    }
}
