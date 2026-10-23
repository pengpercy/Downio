using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Downio.Models;

public sealed class Ed2kSearchResults
{
    public string Gid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool MoreResults { get; set; }
    public List<Ed2kSearchResult> Results { get; set; } = new();
}

// Plain properties (not [ObservableProperty]) so the System.Text.Json source generator can see them;
// the mutable ones raise PropertyChanged by hand so result rows can update in place while polling.
public sealed class Ed2kSearchResult : INotifyPropertyChanged
{
    private string _sourceCount = "0";
    private string _completeSourceCount = "0";
    private bool _isAdded;
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Hash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Length { get; set; } = "0";
    public string FileType { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string SourceNetwork { get; set; } = string.Empty;
    public string Ed2kLink { get; set; } = string.Empty;

    public string SourceCount
    {
        get => _sourceCount;
        set
        {
            if (!SetField(ref _sourceCount, value)) return;
            OnPropertyChanged(nameof(SourceCountValue));
            OnPropertyChanged(nameof(HasManySources));
            OnPropertyChanged(nameof(HasSomeSources));
        }
    }

    public string CompleteSourceCount
    {
        get => _completeSourceCount;
        set
        {
            if (SetField(ref _completeSourceCount, value)) OnPropertyChanged(nameof(CompleteSourceCountValue));
        }
    }

    /// <summary>Already queued for download during this session.</summary>
    [JsonIgnore]
    public bool IsAdded
    {
        get => _isAdded;
        set => SetField(ref _isAdded, value);
    }

    /// <summary>Checked for a batch download.</summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    [JsonIgnore]
    public string Key => string.IsNullOrWhiteSpace(Hash) ? Ed2kLink : Hash;

    [JsonIgnore]
    public int SourceCountValue => int.TryParse(SourceCount, out var count) ? count : 0;

    [JsonIgnore]
    public int CompleteSourceCountValue => int.TryParse(CompleteSourceCount, out var count) ? count : 0;

    [JsonIgnore]
    public long SizeBytes => long.TryParse(Length, out var bytes) ? bytes : 0;

    // Availability buckets used to color the source indicator.
    [JsonIgnore]
    public bool HasManySources => SourceCountValue >= 20;

    [JsonIgnore]
    public bool HasSomeSources => SourceCountValue is >= 3 and < 20;

    [JsonIgnore]
    public string EffectiveExtension => Ed2kFileTypeClassifier.GetExtension(Extension, Name);

    [JsonIgnore]
    public string FileTypeCategory => Ed2kFileTypeClassifier.GetCategory(FileType, Extension, Name);

    [JsonIgnore]
    public string FileTypeDisplayKey => FileTypeCategory switch
    {
        "audio" => "Ed2kTypeAudio",
        "video" => "Ed2kTypeVideo",
        "image" => "Ed2kTypeImage",
        "doc" => "Ed2kTypeDocument",
        "archive" => "Ed2kTypeArchive",
        _ => string.IsNullOrWhiteSpace(EffectiveExtension) ? "Ed2kTypeFile" : EffectiveExtension.ToUpperInvariant()
    };

    [JsonIgnore]
    public string ExtensionBadge => string.IsNullOrWhiteSpace(EffectiveExtension) ? "--" : EffectiveExtension.ToUpperInvariant();

    public string SizeText
    {
        get
        {
            if (!long.TryParse(Length, out var bytes) || bytes < 0) return "--";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024d:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024:F1} MB";
            return $"{bytes / 1024d / 1024 / 1024:F2} GB";
        }
    }

    /// <summary>Copies the counters that change between polls of the same search.</summary>
    public void UpdateCountsFrom(Ed2kSearchResult other)
    {
        SourceCount = other.SourceCount;
        CompleteSourceCount = other.CompleteSourceCount;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
