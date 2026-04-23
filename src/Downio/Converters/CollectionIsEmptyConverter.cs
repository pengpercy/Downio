using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Downio.Converters;

public class CollectionIsEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = value switch
        {
            null => true,
            int i => i == 0,
            long l => l == 0,
            ICollection c => c.Count == 0,
            IEnumerable e => !e.GetEnumerator().MoveNext(),
            _ => true
        };

        if (parameter is string s && (string.Equals(s, "invert", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "not", StringComparison.OrdinalIgnoreCase)))
        {
            isEmpty = !isEmpty;
        }

        return isEmpty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
