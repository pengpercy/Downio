using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Downio.Models;

namespace Downio.Services;

/// <summary>
/// Persists terminal aria2 tasks because aria2 only retains completed/error results in memory.
/// </summary>
public sealed class StoppedTaskHistoryService
{
    private const int MaxEntries = 1000;
    private readonly string _path;
    private readonly Dictionary<string, StoppedTaskHistoryItem> _items;

    public StoppedTaskHistoryService()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Downio");
        _path = Path.Combine(directory, "stopped-tasks.json");
        _items = Load(_path);
    }

    public void SyncWithAria2(IEnumerable<DownloadTask> tasks)
    {
        var changed = false;
        foreach (var task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                continue;
            }

            if (IsTerminal(task.Status))
            {
                if (!_items.TryGetValue(task.Id, out var existing) || !existing.Matches(task))
                {
                    _items[task.Id] = StoppedTaskHistoryItem.From(task);
                    changed = true;
                }
            }
            else if (_items.Remove(task.Id))
            {
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    public IReadOnlyList<DownloadTask> GetTasksExcept(ISet<string> activeIds)
    {
        return _items.Values
            .Where(item => !activeIds.Contains(item.Id))
            .OrderByDescending(item => item.FinishedAt)
            .Select(item => item.ToDownloadTask())
            .ToList();
    }

    public void Remove(string id)
    {
        if (!string.IsNullOrWhiteSpace(id) && _items.Remove(id))
        {
            Save();
        }
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Save();
    }

    private static bool IsTerminal(string status) =>
        status is "StatusStopped" or "StatusCompleted" or "StatusError";

    private static Dictionary<string, StoppedTaskHistoryItem> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, StoppedTaskHistoryItem>(StringComparer.Ordinal);
            }

            var items = JsonSerializer.Deserialize(File.ReadAllText(path), StoppedTaskHistoryJsonContext.Default.ListStoppedTaskHistoryItem)
                ?? [];
            return items
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.FinishedAt).First(), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to load stopped download task history");
            return new Dictionary<string, StoppedTaskHistoryItem>(StringComparer.Ordinal);
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var items = _items.Values
                .OrderByDescending(item => item.FinishedAt)
                .Take(MaxEntries)
                .ToList();
            _items.Clear();
            foreach (var item in items)
            {
                _items[item.Id] = item;
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(items, StoppedTaskHistoryJsonContext.Default.ListStoppedTaskHistoryItem));
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to save stopped download task history");
        }
    }
}

internal sealed class StoppedTaskHistoryItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public double Progress { get; set; }
    public string Status { get; set; } = "StatusStopped";
    public int Split { get; set; } = 1;
    public string Url { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<string> FilePaths { get; set; } = [];
    public DateTimeOffset FinishedAt { get; set; }

    public bool Matches(DownloadTask task) =>
        Name == task.Name &&
        TotalBytes == task.TotalBytes &&
        DownloadedBytes == task.DownloadedBytes &&
        Progress.Equals(task.Progress) &&
        Status == task.Status &&
        Split == task.Split &&
        Url == task.Url &&
        FilePath == task.FilePath &&
        FilePaths.SequenceEqual(task.FilePaths, StringComparer.Ordinal);

    public static StoppedTaskHistoryItem From(DownloadTask task) => new()
    {
        Id = task.Id,
        Name = task.Name,
        TotalBytes = task.TotalBytes,
        DownloadedBytes = task.DownloadedBytes,
        Progress = task.Progress,
        Status = task.Status,
        Split = task.Split,
        Url = task.Url,
        FilePath = task.FilePath,
        FilePaths = task.FilePaths.ToList(),
        FinishedAt = DateTimeOffset.UtcNow
    };

    public DownloadTask ToDownloadTask() => new()
    {
        Id = Id,
        Name = Name,
        TotalBytes = TotalBytes,
        DownloadedBytes = DownloadedBytes,
        Progress = Progress,
        Status = Status,
        Speed = "0 B/s",
        DownloadSpeedBytesPerSecond = 0,
        TimeLeft = string.Empty,
        Connections = 0,
        Split = Split,
        Url = Url,
        FilePath = FilePath,
        FilePaths = FilePaths.ToList()
    };
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<StoppedTaskHistoryItem>))]
internal partial class StoppedTaskHistoryJsonContext : JsonSerializerContext
{
}
