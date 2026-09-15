using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace MoreCharacterLines;

/// <summary>
/// 一次性播报的记录（比如“壶铃练满”那句只播一次）。
///
/// 存在 user://MoreCharacterLines/state.json —— 即
/// %AppData%\SlayTheSpire2\MoreCharacterLines\state.json，
/// 所以读档、重启游戏之后也不会重复播报。
/// </summary>
internal static class OneShot
{
    private const string StateDirPath = "user://MoreCharacterLines";
    private const string StateFilePath = "user://MoreCharacterLines/state.json";

    /// <summary>最多记多少条，防止文件无限增长。</summary>
    private const int MaxEntries = 200;

    private static readonly object Gate = new();
    private static List<string>? _entries;
    private static HashSet<string>? _lookup;

    internal static bool IsDone(string key)
    {
        lock (Gate)
        {
            EnsureLoaded();
            return _lookup!.Contains(key);
        }
    }

    internal static void MarkDone(string key)
    {
        lock (Gate)
        {
            EnsureLoaded();
            if (!_lookup!.Add(key)) return;

            _entries!.Add(key);
            while (_entries.Count > MaxEntries) _entries.RemoveAt(0);

            Save();
        }
    }

    private static void EnsureLoaded()
    {
        if (_entries is not null) return;

        _entries = new List<string>();
        _lookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string? json = LineBank.ReadText(StateFilePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            json = json.TrimStart('\uFEFF', '\u200B', '\u0000');
            List<string>? loaded = JsonSerializer.Deserialize<List<string>>(json);
            if (loaded is null) return;

            foreach (string entry in loaded)
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                _entries.Add(entry);
                _lookup.Add(entry);
            }
        }
        catch (Exception e)
        {
            Log.Warn("[MoreCharacterLines] 读取一次性播报记录失败（当作空处理）：" + e.Message);
        }
    }

    private static void Save()
    {
        try
        {
            Godot.DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(StateDirPath));
            string json = JsonSerializer.Serialize(_entries);
            LineBank.TryWriteText(StateFilePath, json);
        }
        catch (Exception e)
        {
            Log.Warn("[MoreCharacterLines] 保存一次性播报记录失败：" + e.Message);
        }
    }
}
