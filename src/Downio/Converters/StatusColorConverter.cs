using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Downio.Converters;

public class StatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status switch
            {
                "StatusDownloading" => Brushes.DodgerBlue,
                "StatusCompleted" => Brushes.ForestGreen, // Or our theme green #508252
                "StatusPaused" => Brushes.Orange,
                "StatusError" => Brushes.Red,
                _ => Brushes.Gray
            };
        }
        return Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
