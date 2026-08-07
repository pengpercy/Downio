using System;
using System.Collections.Generic;
using System.IO;

namespace Downio.Models;

public static class Ed2kFileTypeClassifier
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "mp3", "flac", "wav", "m4a", "aac", "ogg", "wma", "ape" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "mp4", "mkv", "avi", "mov", "wmv", "flv", "webm", "m4v", "mpg", "mpeg", "ts" };
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "pdf", "txt", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "epub", "mobi" };
    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "iso" };
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "jpg", "jpeg", "png", "gif", "webp", "bmp", "tiff", "svg", "heic" };

    public static string GetExtension(string? extension, string? fileName)
    {
        var normalized = (extension ?? string.Empty).Trim().TrimStart('.');
        if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        return Path.GetExtension(fileName ?? string.Empty).TrimStart('.');
    }

    public static string GetCategory(string? fileType, string? extension, string? fileName = null)
    {
        var type = fileType?.Trim() ?? string.Empty;
        var ext = GetExtension(extension, fileName);
        if (type.Contains("audio", StringComparison.OrdinalIgnoreCase) || AudioExtensions.Contains(ext)) return "audio";
        if (type.Contains("video", StringComparison.OrdinalIgnoreCase) || VideoExtensions.Contains(ext)) return "video";
        if (type.Contains("image", StringComparison.OrdinalIgnoreCase) || ImageExtensions.Contains(ext)) return "image";
        if (type.Contains("doc", StringComparison.OrdinalIgnoreCase) || DocumentExtensions.Contains(ext)) return "doc";
        if (type.Contains("archive", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("compressed", StringComparison.OrdinalIgnoreCase) || ArchiveExtensions.Contains(ext)) return "archive";
        return "file";
    }
}
