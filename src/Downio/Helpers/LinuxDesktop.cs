using System;
using Downio.Services;

namespace Downio.Helpers;

public static class LinuxDesktop
{
    /// <summary>
    /// Whether Avalonia draws the title bar itself on Linux instead of the window manager. Follows the
    /// "custom title bar" setting (on by default); DOWNIO_LINUX_CSD=0/1 overrides it. Read once, as
    /// Avalonia's X11 options are fixed at startup.
    /// </summary>
    public static bool UsesDrawnDecorations { get; } = DetectDrawnDecorations();

    private static bool DetectDrawnDecorations()
    {
        if (!OperatingSystem.IsLinux()) return false;

        switch (Environment.GetEnvironmentVariable("DOWNIO_LINUX_CSD"))
        {
            case "0": return false;
            case "1": return true;
        }

        return new SettingsService().Settings.LinuxDrawnTitleBar;
    }
}
