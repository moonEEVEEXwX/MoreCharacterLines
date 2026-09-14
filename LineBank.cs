using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace CharacterLines;

/// <summary>场景 ID。加新场景时在这里补一个常量，再写一个对应的补丁文件。</summary>
internal static class Scenes
{
    /// <summary>火堆（休息处）：顶部那句提示语。</summary>
    internal const string RestSite = "rest_site";

    /// <summary>多人模式催促 / ping —— JSON 里已经留好位置，代码还没接。</summary>
    internal const string Ping = "ping";
}

/// <summary>单个场景：角色 → 各个池子的台词。</summary>
internal sealed class LineScene
{
    /// <summary>角色 ID / "DEFAULT" → （池名 → 台词）。池名如 normal / low_hp。</summary>
    internal readonly Dictionary<string, Dictionary<string, List<string>>> Characters =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>场景级条件概率覆盖（可选，优先级高于文件级 _conditionChance）。</summary>
    internal readonly Dictionary<string, double> ConditionChances = new(StringComparer.OrdinalIgnoreCase);

    internal List<string>? Pool(string? characterId, string pool)
    {
        if (string.IsNullOrWhiteSpace(characterId)) return null;
        if (!Characters.TryGetValue(characterId, out Dictionary<string, List<string>>? pools)) return null;
        return pools.TryGetValue(pool, out List<string>? lines) ? lines : null;
    }

    internal List<string>? DefaultPool(string pool, string[] defaultKeys)
    {
        foreach (string key in defaultKeys)
        {
            List<string>? lines = Pool(key, pool);
            if (lines is not null) return lines;
        }
        return null;
    }
}

/// <summary>一整份台词文件。</summary>
internal sealed class LineFile
{
    /// <summary>有专属台词的角色抽到 DEFAULT 通用池的概率（_defaultChance）。</summary>
    internal double DefaultChance = LineBank.DefaultChanceFallback;

    /// <summary>文件级条件概率（_conditionChance.&lt;条件名&gt; = 概率）。</summary>
    internal readonly Dictionary<string, double> ConditionChances = new(StringComparer.OrdinalIgnoreCase);

    internal readonly Dictionary<string, LineScene> Scenes = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 台词库：读 JSON、按「场景 + 角色 + 可选条件」抽一句。
///
/// 台词文件来源（按顺序，先命中先用）：
///   1. &lt;mod 文件夹&gt;\lines.json            —— 和 dll/pck 放一起，下载到 mod 的人可以直接就地改
///   2. user://CharacterLines/lines.json     —— %AppData%/SlayTheSpire2/CharacterLines/lines.json
///   3. res://CharacterLines/lines.json      —— mod 的 PCK 里自带的那份
///   4. 都没有就返回 null（调用方保持游戏原样）
///
/// JSON 结构（条件直接挂在角色下面）：
///   {
///     "_defaultChance": 0.1,
///     "_conditionChance": { "low_hp": 0.8 },
///     "rest_site": {
///       "IRONCLAD": {
///         "normal": ["平时说的话...", "..."],
///         "low_hp": ["血量低时说的话...", "..."]
///       },
///       "DEFAULT": {
///         "normal": ["...", "..."],
///         "low_hp": ["..."]
///       },
///       "SILENT": ["只写数组也行 —— 等于只有 normal 池"]
///     },
///     "ping": { "IRONCLAD": ["快点。"] }
///   }
///
/// 还兼容旧写法：场景里的 "_conditions": { "low_hp": { "_chance": 0.8, "IRONCLAD": [...] } }。
/// </summary>
internal static class LineBank
{
    internal const string DefaultKey = "DEFAULT";

    /// <summary>平时的池子名。也认 default / 正常 / 平时 这些写法。</summary>
    internal const string NormalPool = "normal";

    /// <summary>有专属台词的角色抽到 DEFAULT 通用池的默认概率（可用 _defaultChance 覆盖）。</summary>
    internal const double DefaultChanceFallback = 0.1;

    private static readonly string[] DefaultKeys = { "DEFAULT", "default", "通用", "默认" };
    private static readonly string[] NormalAliases = { "normal", "default", "正常", "平时" };

    private const string BuiltInPath = "res://CharacterLines/lines.json";
    private const string UserDirPath = "user://CharacterLines";
    private const string UserFilePath = "user://CharacterLines/lines.json";
    private const string EditableFileName = "lines.json";

    private static bool _loggedSource;
    private static bool _modFolderResolved;
    private static string? _modFolderFilePath;

    /// <summary>
    /// 抽一句台词。conditions 按顺序尝试（例如 "low_hp"）；
    /// 返回 null 表示这个场景/角色没有可用台词，调用方保持原样即可。
    /// </summary>
    internal static string? Pick(string scene, string? characterId, params string[] conditions)
    {
        return Pick(scene, characterId, out _, conditions);
    }

    /// <summary>
    /// 同上，但额外告诉你这次命中的是哪个条件（没有命中条件池就是 null）。
    /// 用于“只播一次”这种需要知道到底播了哪句的场景。
    /// </summary>
    internal static string? Pick(string scene, string? characterId, out string? usedCondition, params string[] conditions)
    {
        usedCondition = null;
        EnsureEditableFile();

        LineFile? file = LoadFirstAvailable();
        if (file is null) return null;
        if (!file.Scenes.TryGetValue(scene, out LineScene? lineScene)) return null;

        // 1) 条件池优先（比如血量低时说的话）
        if (conditions is not null)
        {
            foreach (string condition in conditions)
            {
                if (string.IsNullOrWhiteSpace(condition)) continue;

                double chance = ResolveConditionChance(lineScene, file, condition);
                if (chance <= 0.0) continue;
                if (chance < 1.0 && Random.Shared.NextDouble() >= chance) continue;

                string? conditioned = Draw(lineScene, characterId, condition, file.DefaultChance);
                if (conditioned is not null)
                {
                    usedCondition = condition;
                    return conditioned; // 条件池里没写这个角色 → 继续找下一个
                }
            }
        }

        // 2) 平时的池子
        return Draw(lineScene, characterId, NormalPool, file.DefaultChance);
    }

    private static double ResolveConditionChance(LineScene scene, LineFile file, string condition)
    {
        if (scene.ConditionChances.TryGetValue(condition, out double sceneChance)) return sceneChance;
        if (file.ConditionChances.TryGetValue(condition, out double fileChance)) return fileChance;
        return 1.0;
    }

    /// <summary>从某个池子里抽：优先该角色，其次 DEFAULT（并按概率混用）。</summary>
    private static string? Draw(LineScene scene, string? characterId, string pool, double defaultChance)
    {
        List<string>? characterLines = scene.Pool(characterId, pool);
        List<string>? defaults = scene.DefaultPool(pool, DefaultKeys);

        bool useDefaults = characterLines is null || characterLines.Count == 0;
        if (!useDefaults && defaults is { Count: > 0 } && Random.Shared.NextDouble() < defaultChance)
            useDefaults = true;

        List<string>? chosen = useDefaults ? defaults : characterLines;
        chosen ??= characterLines ?? defaults;
        if (chosen is null || chosen.Count == 0) return null;

        return chosen[Random.Shared.Next(chosen.Count)];
    }

    // ── 可编辑文件 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 确保有「可直接编辑」的台词文件：优先放在 mod 文件夹里（就地定制），
    /// 写不进去再退到 %AppData%。
    /// </summary>
    internal static void EnsureEditableFile()
    {
        try
        {
            string? modFolderFile = ModFolderFilePath();
            if (modFolderFile is not null && FileExists(modFolderFile)) return;
            if (FileExists(UserFilePath)) return;

            string? builtIn = ReadText(BuiltInPath);
            if (string.IsNullOrEmpty(builtIn)) return;

            if (modFolderFile is not null && TryWriteText(modFolderFile, builtIn))
            {
                Log.Info("[CharacterLines] 已生成可编辑台词文件：" + modFolderFile);
                return;
            }

            try
            {
                Godot.DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(UserDirPath));
            }
            catch
            {
                // 目录建不出来就算了，下面写入自然会失败
            }

            if (TryWriteText(UserFilePath, builtIn))
            {
                Log.Info("[CharacterLines] 已生成可编辑台词文件：" + ProjectSettings.GlobalizePath(UserFilePath));
            }
        }
        catch (Exception e)
        {
            Log.Warn("[CharacterLines] 生成台词文件失败：" + e.Message);
        }
    }

    private static string? ModFolderFilePath()
    {
        if (_modFolderResolved) return _modFolderFilePath;
        _modFolderResolved = true;

        try
        {
            string? dllPath = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(dllPath))
            {
                string? dir = System.IO.Path.GetDirectoryName(dllPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    _modFolderFilePath = System.IO.Path.Combine(dir, EditableFileName);
                }
            }
        }
        catch
        {
            _modFolderFilePath = null;
        }

        return _modFolderFilePath;
    }

    // ── 读取 ────────────────────────────────────────────────────────────────

    private static LineFile? LoadFirstAvailable()
    {
        foreach (string path in CandidatePaths())
        {
            string? json = ReadText(path);
            if (string.IsNullOrEmpty(json)) continue;

            LineFile? parsed = Parse(json);
            if (parsed is { Scenes.Count: > 0 })
            {
                LogSourceOnce(path);
                return parsed;
            }

            Log.Warn("[CharacterLines] 台词文件解析失败，尝试下一份：" + path);
        }

        return null;
    }

    private static IEnumerable<string> CandidatePaths()
    {
        string? modFolderFile = ModFolderFilePath();
        if (modFolderFile is not null) yield return modFolderFile;
        yield return UserFilePath;
        yield return BuiltInPath;
    }

    /// <summary>res:// user:// 走 Godot 的虚拟文件系统；绝对路径（mod 文件夹）走 System.IO。</summary>
    private static bool IsGodotPath(string path)
    {
        return path.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("user://", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool FileExists(string path)
    {
        try
        {
            return IsGodotPath(path) ? Godot.FileAccess.FileExists(path) : System.IO.File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    internal static string? ReadText(string path)
    {
        try
        {
            if (!FileExists(path)) return null;

            if (!IsGodotPath(path))
            {
                return System.IO.File.ReadAllText(path); // 默认 UTF-8
            }

            using Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            return file?.GetAsText();
        }
        catch (Exception e)
        {
            Log.Warn("[CharacterLines] 读取失败 " + path + "：" + e.Message);
            return null;
        }
    }

    internal static bool TryWriteText(string path, string text)
    {
        try
        {
            // 带 UTF-8 BOM 写：记事本 / PowerShell 等 Windows 工具能正确识别中文；读取时会去掉 BOM。
            if (!IsGodotPath(path))
            {
                System.IO.File.WriteAllText(path, text, new System.Text.UTF8Encoding(true));
                return true;
            }

            using Godot.FileAccess? file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            if (file is null) return false;
            file.StoreString("\uFEFF" + text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── 解析 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 宽容解析：允许 BOM、// 注释、结尾多余逗号；
    /// 台词写成字符串或字符串数组都行；_ 开头的键当注释（除了 _defaultChance / _conditionChance / _conditions）。
    /// </summary>
    private static LineFile? Parse(string json)
    {
        try
        {
            json = json.TrimStart('\uFEFF', '\u200B', '\u0000');

            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var file = new LineFile();
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                string key = property.Name;

                if (string.Equals(key, "_defaultChance", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out double chance))
                    {
                        file.DefaultChance = Math.Clamp(chance, 0.0, 1.0);
                    }
                    continue;
                }

                if (string.Equals(key, "_conditionChance", StringComparison.OrdinalIgnoreCase))
                {
                    ParseChanceMap(property.Value, file.ConditionChances);
                    continue;
                }

                if (key.StartsWith("_", StringComparison.Ordinal)) continue;
                if (property.Value.ValueKind != JsonValueKind.Object) continue;

                LineScene scene = ParseScene(property.Value);
                if (scene.Characters.Count > 0) file.Scenes[key] = scene;
            }

            return file;
        }
        catch (JsonException e)
        {
            Log.Warn("[CharacterLines] JSON 格式有误：" + e.Message);
            return null;
        }
    }

    private static LineScene ParseScene(JsonElement sceneElement)
    {
        var scene = new LineScene();

        foreach (JsonProperty property in sceneElement.EnumerateObject())
        {
            string key = property.Name;

            // 场景级条件概率（可选）
            if (string.Equals(key, "_conditionChance", StringComparison.OrdinalIgnoreCase))
            {
                ParseChanceMap(property.Value, scene.ConditionChances);
                continue;
            }

            // 旧写法兼容：_conditions: { low_hp: { _chance, IRONCLAD: [...], DEFAULT: [...] } }
            if (string.Equals(key, "_conditions", StringComparison.OrdinalIgnoreCase))
            {
                ParseLegacyConditions(property.Value, scene);
                continue;
            }

            if (key.StartsWith("_", StringComparison.Ordinal)) continue;

            JsonElement value = property.Value;

            // 形式一：["...", "..."] 或 "..."  —— 只有 normal 池
            if (value.ValueKind is JsonValueKind.Array or JsonValueKind.String)
            {
                List<string>? lines = ParseLines(value);
                if (lines is { Count: > 0 }) SetPool(scene, key, NormalPool, lines);
                continue;
            }

            // 形式二：{ "normal": [...], "low_hp": [...] }  —— 条件直接挂在角色下面
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty poolProperty in value.EnumerateObject())
                {
                    if (poolProperty.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                    List<string>? lines = ParseLines(poolProperty.Value);
                    if (lines is { Count: > 0 }) SetPool(scene, key, NormalizePoolName(poolProperty.Name), lines);
                }
            }
        }

        return scene;
    }

    private static void ParseLegacyConditions(JsonElement element, LineScene scene)
    {
        if (element.ValueKind != JsonValueKind.Object) return;

        foreach (JsonProperty conditionProperty in element.EnumerateObject())
        {
            if (conditionProperty.Value.ValueKind != JsonValueKind.Object) continue;
            string conditionName = conditionProperty.Name;

            foreach (JsonProperty poolProperty in conditionProperty.Value.EnumerateObject())
            {
                if (string.Equals(poolProperty.Name, "_chance", StringComparison.OrdinalIgnoreCase))
                {
                    if (poolProperty.Value.ValueKind == JsonValueKind.Number && poolProperty.Value.TryGetDouble(out double chance))
                    {
                        scene.ConditionChances[conditionName] = Math.Clamp(chance, 0.0, 1.0);
                    }
                    continue;
                }

                if (poolProperty.Name.StartsWith("_", StringComparison.Ordinal)) continue;

                List<string>? lines = ParseLines(poolProperty.Value);
                if (lines is { Count: > 0 }) SetPool(scene, poolProperty.Name, conditionName, lines);
            }
        }
    }

    private static void ParseChanceMap(JsonElement element, Dictionary<string, double> target)
    {
        if (element.ValueKind != JsonValueKind.Object) return;

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out double chance))
            {
                target[property.Name] = Math.Clamp(chance, 0.0, 1.0);
            }
        }
    }

    private static void SetPool(LineScene scene, string characterId, string pool, List<string> lines)
    {
        if (!scene.Characters.TryGetValue(characterId, out Dictionary<string, List<string>>? pools))
        {
            pools = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            scene.Characters[characterId] = pools;
        }

        pools[pool] = lines;
    }

    /// <summary>normal / default / 正常 / 平时 都算「平时的池子」。</summary>
    private static string NormalizePoolName(string name)
    {
        foreach (string alias in NormalAliases)
        {
            if (string.Equals(name, alias, StringComparison.OrdinalIgnoreCase)) return NormalPool;
        }
        return name;
    }

    /// <summary>台词值：字符串数组，或单个字符串。</summary>
    private static List<string>? ParseLines(JsonElement element)
    {
        var list = new List<string>();

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? line = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (!string.IsNullOrWhiteSpace(line)) list.Add(line!);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            string? line = element.GetString();
            if (!string.IsNullOrWhiteSpace(line)) list.Add(line!);
        }

        return list.Count > 0 ? list : null;
    }

    private static void LogSourceOnce(string source)
    {
        if (_loggedSource) return;
        _loggedSource = true;
        Log.Info("[CharacterLines] 台词来源：" + source);
    }
}
