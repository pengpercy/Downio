using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Downio.Models;

namespace Downio.Converters;

public sealed class Ed2kFileTypeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = value switch
        {
            Ed2kSearchResult result => result.FileTypeCategory,
            string type => Ed2kFileTypeClassifier.GetCategory(type, null),
            _ => "file"
        };
        var resourceKey = category switch
        {
            "audio" => "IconFileAudio",
            "video" => "IconFileVideo",
            "image" => "IconFileImage",
            "doc" => "IconFileDocument",
            "archive" => "IconFileArchive",
            _ => "IconFileGeneric"
        };
        return Application.Current?.TryGetResource(resourceKey, null, out var icon) == true ? icon : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
