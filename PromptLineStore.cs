using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace RestSitePrompts;

/// <summary>
/// 台词库：读 JSON、按角色抽一句。
///
/// 台词来源（按顺序，先命中先用）：
///   1. &lt;mod 文件夹&gt;\lines.json
///      → 和 dll/pck 放在一起，随 mod 一起发布，下载到 mod 的人可以直接就地改
///   2. user://RestSitePrompts/lines.json
///      → 真实路径 %AppData%/SlayTheSpire2/RestSitePrompts/lines.json
///      → mod 文件夹不可写（例如创意工坊安装）时，自动改用这里
///   3. res://RestSitePrompts/rest_site_prompts.json
///      → mod 的 PCK 里自带的那份（assets/RestSitePrompts/rest_site_prompts.json）
///   4. 都没有就返回 null，游戏保持原版那句“我该做什么呢？”
///
/// 前两份都会在需要时自动生成（改完重进火堆即生效，不用重新编译）。
///
/// JSON 结构（键 = 角色 ID，值 = 台词数组；DEFAULT 为通用台词）：
///   {
///     "IRONCLAD": ["稍作调整……继续杀戮……", "稍息……前进……"],
///     "DEFAULT":  ["我该做什么呢……", "接下来做什么好？"]
///   }
/// </summary>
internal static class PromptLineStore
{
    /// <summary>通用台词的键名（大小写、中英文写法都认）。</summary>
    private static readonly string[] DefaultKeys = { "DEFAULT", "default", "通用", "默认" };

    private const string BuiltInPath = "res://RestSitePrompts/rest_site_prompts.json";
    private const string UserDirPath = "user://RestSitePrompts";
    private const string UserFilePath = "user://RestSitePrompts/lines.json";
    private const string EditableFileName = "lines.json";

    /// <summary>
    /// 有专属台词的角色，仍有这个概率抽到 DEFAULT 里的通用台词（0.25 = 25%）。
    /// 想让角色永远只说自己的台词就改成 0；想更多通用味就调大（最高 1）。
    /// </summary>
    private const double DefaultLineChance = 0.25;

    private static bool _loggedSource;
    private static bool _modFolderResolved;
    private static string? _modFolderFilePath;

    /// <summary>按角色 ID 抽一句台词；返回 null 表示不加干预（沿用原版文案）。</summary>
    internal static string? Pick(string? characterId)
    {
        // 顺带确保“可编辑台词文件”存在（进火堆时补生成，不依赖初始化时机）。
        EnsureEditableFile();

        Dictionary<string, List<string>>? lines = LoadFirstAvailable();
        if (lines is null) return null;

        List<string>? characterLines = Find(lines, characterId);
        List<string>? defaults = FindAny(lines, DefaultKeys);

        bool useDefaults = characterLines is null || characterLines.Count == 0;
        if (!useDefaults && defaults is { Count: > 0 } && Random.Shared.NextDouble() < DefaultLineChance)
            useDefaults = true;

        List<string>? pool = useDefaults ? defaults : characterLines;
        pool ??= characterLines ?? defaults;
        if (pool is null || pool.Count == 0) return null;

        return pool[Random.Shared.Next(pool.Count)];
    }

    /// <summary>
    /// 确保有“可直接编辑”的台词文件：
    /// 优先在 mod 文件夹里生成（就地定制），写不进去再退到 %AppData%。
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
                Log.Info("[RestSitePrompts] 已生成可编辑台词文件：" + modFolderFile);
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
                Log.Info("[RestSitePrompts] 已生成可编辑台词文件：" + ProjectSettings.GlobalizePath(UserFilePath));
            }
        }
        catch (Exception e)
        {
            Log.Warn("[RestSitePrompts] 生成台词文件失败：" + e.Message);
        }
    }

    // ── 读取 ────────────────────────────────────────────────────────────────

    /// <summary>按优先级找第一份能用的台词。</summary>
    private static Dictionary<string, List<string>>? LoadFirstAvailable()
    {
        foreach (string path in CandidatePaths())
        {
            string? json = ReadText(path);
            if (string.IsNullOrEmpty(json)) continue;

            Dictionary<string, List<string>>? parsed = Parse(json);
            if (parsed is { Count: > 0 })
            {
                LogSourceOnce(path);
                return parsed;
            }

            Log.Warn("[RestSitePrompts] 台词文件解析失败，尝试下一份：" + path);
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

    /// <summary>mod 文件夹（本 DLL 所在目录）里的 lines.json 路径；拿不到就返回 null。</summary>
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

    /// <summary>res:// user:// 走 Godot 的虚拟文件系统；绝对路径（mod 文件夹）走 System.IO，更直接也更稳。</summary>
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
            Log.Warn("[RestSitePrompts] 读取失败 " + path + "：" + e.Message);
            return null;
        }
    }

    private static bool TryWriteText(string path, string text)
    {
        try
        {
            // 带 UTF-8 BOM 写：记事本 / PowerShell 等 Windows 工具能正确识别中文；
            // 读取端（解析前）会把 BOM 去掉。
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

    /// <summary>宽容解析：允许 BOM、// 注释和结尾多余逗号；值写成字符串或字符串数组都行。</summary>
    private static Dictionary<string, List<string>>? Parse(string json)
    {
        try
        {
            // 有些编辑器（或 Godot 的 GetAsText）会把 BOM / 零宽字符留在开头，先去掉
            json = json.TrimStart('\uFEFF', '\u200B', '\u0000');

            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
            {
                // 以 _ 开头的键当作注释性字段（比如 "_comment"），不当台词
                if (property.Name.StartsWith("_", StringComparison.Ordinal)) continue;

                var list = new List<string>();
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in property.Value.EnumerateArray())
                    {
                        string? line = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(line)) list.Add(line!);
                    }
                }
                else if (property.Value.ValueKind == JsonValueKind.String)
                {
                    string? line = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(line)) list.Add(line!);
                }

                if (list.Count > 0) result[property.Name] = list;
            }

            return result;
        }
        catch (JsonException e)
        {
            Log.Warn("[RestSitePrompts] JSON 格式有误：" + e.Message);
            return null;
        }
    }

    // ── 小工具 ──────────────────────────────────────────────────────────────

    private static List<string>? Find(Dictionary<string, List<string>> lines, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (KeyValuePair<string, List<string>> pair in lines)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }
        return null;
    }

    private static List<string>? FindAny(Dictionary<string, List<string>> lines, string[] keys)
    {
        foreach (string key in keys)
        {
            List<string>? found = Find(lines, key);
            if (found is not null) return found;
        }
        return null;
    }

    private static void LogSourceOnce(string source)
    {
        if (_loggedSource) return;
        _loggedSource = true;
        Log.Info("[RestSitePrompts] 台词来源：" + source);
    }
}
