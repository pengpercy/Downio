using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Downio.Models;

public sealed class Ed2kSearchResults
{
    public string Gid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool MoreResults { get; set; }
    public List<Ed2kSearchResult> Results { get; set; } = new();
}

public sealed class Ed2kSearchResult
{
    public string Hash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Length { get; set; } = "0";
    public string SourceCount { get; set; } = "0";
    public string CompleteSourceCount { get; set; } = "0";
    public string FileType { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string SourceNetwork { get; set; } = string.Empty;
    public string Ed2kLink { get; set; } = string.Empty;

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
}
