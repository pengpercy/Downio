using System;

namespace Downio.Helpers;

public static class LinuxDesktop
{
    /// <summary>
    /// Whether Avalonia should draw the title bar itself on Linux. Its X11 client-side decorations
    /// are experimental, so they are only enabled on the mainstream desktops; elsewhere the window
    /// manager keeps drawing the title bar. DOWNIO_LINUX_CSD=0/1 overrides the detection.
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

        // e.g. "GNOME", "ubuntu:GNOME", "KDE", "XFCE"
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.ToUpperInvariant() ?? "";
        foreach (var name in desktop.Split(':'))
        {
            if (name is "GNOME" or "KDE" or "XFCE") return true;
        }
        return false;
    }
}
