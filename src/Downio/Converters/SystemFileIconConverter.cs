using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Downio.Helpers;
using Downio.Models;

namespace Downio.Converters;

/// <summary>
/// File name → the file manager's icon for its extension, but only for files our own glyphs
/// don't cover; returns null for recognised types so the glyph is shown instead.
/// </summary>
public sealed class SystemFileIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string fileName) return null;
        if (Ed2kFileTypeClassifier.GetCategory(null, null, fileName) != "file") return null;
        return SystemFileIcon.ForExtension(Ed2kFileTypeClassifier.GetExtension(null, fileName));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
