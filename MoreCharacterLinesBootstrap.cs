using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace MoreCharacterLines;

/// <summary>
/// Mod 入口。游戏启动时扫描所有已加载 DLL，找到带 [ModInitializer] 的类并调用指定静态方法。
///   - PatchAll() 把本程序集里所有 [HarmonyPatch] 类应用上去（目前是 SceneRestSite）
///   - EnsureEditableFile() 在 mod 文件夹（写不进去则 %AppData%）生成可直接编辑的台词文件
/// </summary>
[ModInitializer(nameof(Init))]
public static class Bootstrap
{
    private static bool _initialized;

    public static void Init()
    {
        if (_initialized) return;
        _initialized = true;

        new Harmony("lbd.characterlines").PatchAll(Assembly.GetExecutingAssembly());
        LineBank.EnsureEditableFile();

        Log.Info("[MoreCharacterLines] loaded.");
    }
}
