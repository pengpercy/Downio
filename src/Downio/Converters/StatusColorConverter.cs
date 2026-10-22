using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Globalization;

namespace Downio.Converters;

public class StatusColorConverter : IValueConverter
{
    private static readonly IBrush CompletedBrush = new ImmutableSolidColorBrush(Color.Parse("#33D158"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status switch
            {
                "StatusDownloading" => Brushes.DodgerBlue,
                "StatusCompleted" => CompletedBrush,
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
