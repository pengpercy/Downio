using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Downio.Models;

namespace Downio.Converters;

public class ToastTypeToBrushConverter : IMultiValueConverter
{
    public static readonly ToastTypeToBrushConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count > 0 && values[0] is ToastType type)
        {
            return type switch
            {
                ToastType.Success => Brushes.SeaGreen,
                ToastType.Error => Brushes.IndianRed,
                ToastType.Warning => Brushes.Orange,
                _ => Brushes.CornflowerBlue // Info
            };
        }
        return Brushes.Gray;
    }
}
