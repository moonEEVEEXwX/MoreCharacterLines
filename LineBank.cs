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

/// <summary>单个场景：角色池 + 条件池。</summary>
internal sealed class LineScene
{
    /// <summary>角色 ID / "DEFAULT" → 台词。</summary>
    internal readonly Dictionary<string, List<string>> Pools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>条件名（如 low_hp）→ 条件池。</summary>
    internal readonly Dictionary<string, SceneCondition> Conditions = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>条件池：满足条件时优先从这里抽。</summary>
internal sealed class SceneCondition
{
    /// <summary>满足条件时使用这个池的概率（_chance），默认 1（一定用）。</summary>
    internal double Chance = 1.0;

    internal readonly Dictionary<string, List<string>> Pools = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>一整份台词文件。</summary>
internal sealed class LineFile
{
    /// <summary>有专属台词的角色抽到 DEFAULT 通用池的概率（_defaultChance）。</summary>
    internal double DefaultChance = LineBank.DefaultChanceFallback;

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
/// JSON 结构：
///   {
///     "_defaultChance": 0.1,
///     "rest_site": {
///       "IRONCLAD": ["...", "..."],
///       "DEFAULT":  ["..."],
///       "_conditions": {
///         "low_hp": { "_chance": 0.8, "IRONCLAD": ["..."], "DEFAULT": ["..."] }
///       }
///     }
///   }
/// </summary>
internal static class LineBank
{
    internal const string DefaultKey = "DEFAULT";

    /// <summary>有专属台词的角色抽到 DEFAULT 通用池的默认概率（可在台词文件里用 _defaultChance 覆盖）。</summary>
    internal const double DefaultChanceFallback = 0.1;

    private static readonly string[] DefaultKeys = { "DEFAULT", "default", "通用", "默认" };

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
                if (!lineScene.Conditions.TryGetValue(condition, out SceneCondition? pool)) continue;
                if (pool.Chance < 1.0 && Random.Shared.NextDouble() >= pool.Chance) continue;

                string? conditioned = Draw(pool.Pools, characterId, file.DefaultChance);
                if (conditioned is not null) return conditioned; // 条件池里没写这个角色 → 继续走常规池
            }
        }

        // 2) 常规池
        return Draw(lineScene.Pools, characterId, file.DefaultChance);
    }

    /// <summary>从一组池子里抽：优先该角色，其次 DEFAULT（并按概率混用）。</summary>
    private static string? Draw(Dictionary<string, List<string>> pools, string? characterId, double defaultChance)
    {
        List<string>? characterLines = Find(pools, characterId);
        List<string>? defaults = FindAny(pools, DefaultKeys);

        bool useDefaults = characterLines is null || characterLines.Count == 0;
        if (!useDefaults && defaults is { Count: > 0 } && Random.Shared.NextDouble() < defaultChance)
            useDefaults = true;

        List<string>? pool = useDefaults ? defaults : characterLines;
        pool ??= characterLines ?? defaults;
        if (pool is null || pool.Count == 0) return null;

        return pool[Random.Shared.Next(pool.Count)];
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

    private static bool FileExists(string path)
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

    private static string? ReadText(string path)
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

    private static bool TryWriteText(string path, string text)
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
    /// 台词写成字符串或字符串数组都行；_ 开头的键除 _defaultChance / _conditions / _chance 外都当注释。
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
                if (string.Equals(property.Name, "_defaultChance", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out double chance))
                    {
                        file.DefaultChance = Math.Clamp(chance, 0.0, 1.0);
                    }
                    continue;
                }

                // 其它 _ 开头的键当注释
                if (property.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                if (property.Value.ValueKind != JsonValueKind.Object) continue;

                LineScene scene = ParseScene(property.Value);
                if (scene.Pools.Count > 0 || scene.Conditions.Count > 0) file.Scenes[property.Name] = scene;
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
            if (string.Equals(property.Name, "_conditions", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (JsonProperty conditionProperty in property.Value.EnumerateObject())
                {
                    if (conditionProperty.Value.ValueKind != JsonValueKind.Object) continue;
                    scene.Conditions[conditionProperty.Name] = ParseCondition(conditionProperty.Value);
                }
                continue;
            }

            if (property.Name.StartsWith("_", StringComparison.Ordinal)) continue;

            List<string>? lines = ParseLines(property.Value);
            if (lines is { Count: > 0 }) scene.Pools[property.Name] = lines;
        }

        return scene;
    }

    private static SceneCondition ParseCondition(JsonElement conditionElement)
    {
        var condition = new SceneCondition();
        foreach (JsonProperty property in conditionElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "_chance", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out double chance))
                {
                    condition.Chance = Math.Clamp(chance, 0.0, 1.0);
                }
                continue;
            }

            if (property.Name.StartsWith("_", StringComparison.Ordinal)) continue;

            List<string>? lines = ParseLines(property.Value);
            if (lines is { Count: > 0 }) condition.Pools[property.Name] = lines;
        }

        return condition;
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

    // ── 小工具 ──────────────────────────────────────────────────────────────

    private static List<string>? Find(Dictionary<string, List<string>> pools, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (KeyValuePair<string, List<string>> pair in pools)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }
        return null;
    }

    private static List<string>? FindAny(Dictionary<string, List<string>> pools, string[] keys)
    {
        foreach (string key in keys)
        {
            List<string>? found = Find(pools, key);
            if (found is not null) return found;
        }
        return null;
    }

    private static void LogSourceOnce(string source)
    {
        if (_loggedSource) return;
        _loggedSource = true;
        Log.Info("[CharacterLines] 台词来源：" + source);
    }
}
