using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Downio.Converters;

public class StatusToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var app = Application.Current;
        if (value is string status && app != null)
        {
            // If Downloading or Waiting -> Show Pause button (to pause)
            // If Paused, Stopped, or Error -> Show Play button (to resume/start)
            string iconKey = (status == "StatusDownloading" || status == "StatusWaiting") 
                ? "IconPause" 
                : "IconPlay";

            if (app.TryGetResource(iconKey, null, out var icon))
            {
                return icon;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
