using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace RestSitePrompts;

/// <summary>
/// 火堆（休息处）房间。
///
/// 原版逻辑在 NRestSiteRoom._Ready() 里做了这么一句：
///     Header.SetTextAutoSize(new LocString("rest_site_ui", "PROMPT").GetFormattedText());
/// 也就是把顶部那句“我该做什么呢？”写死成固定文案。
///
/// 这里用 Postfix 在它执行完之后，按「当前角色 + 随机」重写这段文本。
/// 说明：
///   - Header 是顶部那句大字的标签（MegaLabel，会自动缩放字号），
///     而悬停选项时变化的是 Description，两者互不影响，所以悬停不会覆盖我们的台词。
///   - Header 属性在游戏里是私有属性（Godot [Export]），C# 直接点不到，用反射取。
/// </summary>
[HarmonyPatch(typeof(NRestSiteRoom), "_Ready")]
internal static class RestSitePromptHeaderPatch
{
    /// <summary>_runState 是私有字段（IRunState），用 AccessTools 取；失败返回 null 而不是抛异常。</summary>
    private static AccessTools.FieldRef<NRestSiteRoom, IRunState>? _runStateRef;

    /// <summary>私有属性 Header 的 getter，缓存起来避免每次反射查找。</summary>
    private static MethodInfo? _headerGetter;

    private static void Postfix(NRestSiteRoom __instance)
    {
        try
        {
            string? text = PromptLineStore.Pick(CurrentCharacterId(__instance));
            if (string.IsNullOrEmpty(text)) return;

            GetHeader(__instance)?.SetTextAutoSize(text);
        }
        catch (Exception e)
        {
            Log.Warn("[RestSitePrompts] 设置火堆台词失败：" + e.Message);
        }
    }

    private static MegaLabel? GetHeader(NRestSiteRoom room)
    {
        _headerGetter ??= AccessTools.PropertyGetter(typeof(NRestSiteRoom), "Header");
        return _headerGetter?.Invoke(room, null) as MegaLabel;
    }

    /// <summary>取「本地玩家」的角色 ID（IRONCLAD / SILENT / DEFECT / REGENT / NECROBINDER ...）。</summary>
    private static string? CurrentCharacterId(NRestSiteRoom room)
    {
        IRunState? runState = GetRunState(room);
        if (runState is null) return null;

        Player? me = LocalContext.GetMe(runState) ?? runState.Players?.FirstOrDefault();
        return me?.Character?.Id?.Entry;
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
