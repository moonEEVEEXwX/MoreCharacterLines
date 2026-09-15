using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace MoreCharacterLines;

/// <summary>
/// 火堆（休息处）场景。
///
/// 原版逻辑在 NRestSiteRoom._Ready()：
///     Header.SetTextAutoSize(new LocString("rest_site_ui", "PROMPT").GetFormattedText());
/// 也就是把那句“我该做什么呢？”写死。
///
/// 这里用 Postfix 在它之后按「角色 + 条件」重写文案：
///   - 场景 ID：rest_site（对应 lines.json 里的 "rest_site"）
///   - 条件：血量 ≤ 25% 时点亮 low_hp，优先说条件池里的话
/// 悬停选项时改的是 Description 标签，不会覆盖 Header。以后加新场景（比如多人 ping）
/// 照这个文件再写一个补丁即可。
/// </summary>
[HarmonyPatch(typeof(NRestSiteRoom), "_Ready")]
internal static class SceneRestSite
{
    /// <summary>_runState 是私有字段（IRunState），用 AccessTools 取；失败返回 null 而不是抛异常。</summary>
    private static AccessTools.FieldRef<NRestSiteRoom, IRunState>? _runStateRef;

    /// <summary>私有属性 Header 的 getter，缓存起来避免每次反射查找。</summary>
    private static MethodInfo? _headerGetter;

    private static void Postfix(NRestSiteRoom __instance)
    {
        try
        {
            IRunState? runState = GetRunState(__instance);
            Player? player = PlayerContext.FromRunState(runState);

            string? text = LineBank.Pick(
                Scenes.RestSite,
                PlayerContext.CharacterId(player),
                out string? usedCondition,
                RestSiteConditions.For(player, runState));

            if (string.IsNullOrEmpty(text)) return;

            GetHeader(__instance)?.SetTextAutoSize(text);

            // 壶铃练满那句是“只播一次”：真播出去了才记下来，
            // 所以被更高优先级的台词顶掉时会自动顺延到下次火堆。
            if (usedCondition == Conditions.GiryaMaxed)
            {
                OneShot.MarkDone(RestSiteConditions.GiryaAnnouncedKey(player, runState));
            }
        }
        catch (Exception e)
        {
            Log.Warn("[MoreCharacterLines] 设置火堆台词失败：" + e.Message);
        }
    }

    private static MegaLabel? GetHeader(NRestSiteRoom room)
    {
        _headerGetter ??= AccessTools.PropertyGetter(typeof(NRestSiteRoom), "Header");
        return _headerGetter?.Invoke(room, null) as MegaLabel;
    }

    private static IRunState? GetRunState(NRestSiteRoom room)
    {
        try
        {
            _runStateRef ??= AccessTools.FieldRefAccess<NRestSiteRoom, IRunState>("_runState");
            return _runStateRef(room);
        }
        catch
        {
            return null;
        }
    }
}
