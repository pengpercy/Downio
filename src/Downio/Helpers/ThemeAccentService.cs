using System;
using Avalonia;
using Avalonia.Media;

namespace Downio.Helpers;

public static class ThemeAccentService
{
    public static void Apply(string accentMode, string? customAccentHex)
    {
        var app = Application.Current;
        if (app == null) return;

        if (!string.Equals(accentMode, "Custom", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(customAccentHex) ||
            !Color.TryParse(customAccentHex, out var accent))
        {
            ClearOverrides(app);
            return;
        }

        var resources = app.Resources;

        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorDark1"] = Darken(accent, 0.15);
        resources["SystemAccentColorDark2"] = Darken(accent, 0.30);
        resources["SystemAccentColorDark3"] = Darken(accent, 0.45);
        resources["SystemAccentColorLight1"] = Lighten(accent, 0.15);
        resources["SystemAccentColorLight2"] = Lighten(accent, 0.30);
        resources["SystemAccentColorLight3"] = Lighten(accent, 0.45);

        resources["SystemControlHighlightAccentBrush"] = new SolidColorBrush(accent);
        resources["SystemControlForegroundAccentBrush"] = new SolidColorBrush(accent);
        resources["SystemControlBackgroundAccentBrush"] = new SolidColorBrush(accent);
        resources["SystemAccentColorBrush"] = new SolidColorBrush(accent);
    }

    private static void ClearOverrides(Application app)
    {
        var resources = app.Resources;
        resources.Remove("SystemAccentColor");
        resources.Remove("SystemAccentColorDark1");
        resources.Remove("SystemAccentColorDark2");
        resources.Remove("SystemAccentColorDark3");
        resources.Remove("SystemAccentColorLight1");
        resources.Remove("SystemAccentColorLight2");
        resources.Remove("SystemAccentColorLight3");
        resources.Remove("SystemControlHighlightAccentBrush");
        resources.Remove("SystemControlForegroundAccentBrush");
        resources.Remove("SystemControlBackgroundAccentBrush");
        resources.Remove("SystemAccentColorBrush");
    }

    private static Color Lighten(Color c, double amount)
    {
        var r = (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255);
        var g = (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255);
        var b = (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }

    private static Color Darken(Color c, double amount)
    {
        var r = (byte)Math.Clamp(c.R * (1 - amount), 0, 255);
        var g = (byte)Math.Clamp(c.G * (1 - amount), 0, 255);
        var b = (byte)Math.Clamp(c.B * (1 - amount), 0, 255);
        return Color.FromArgb(c.A, r, g, b);
    }
}

