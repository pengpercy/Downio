using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Downio.Services;

public class AutoStartService
{
    private const string AppName = "Downio";

    public void SetAutoStart(bool enable)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetMacAutoStart(enable);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetWindowsAutoStart(enable);
        }
    }

    private void SetMacAutoStart(bool enable)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var launchAgentsDir = Path.Combine(home, "Library", "LaunchAgents");
        var plistPath = Path.Combine(launchAgentsDir, "com.Downio.app.plist");

        if (enable)
        {
            if (!Directory.Exists(launchAgentsDir))
            {
                Directory.CreateDirectory(launchAgentsDir);
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            // Handle .app bundle path if necessary. 
            // Often ProcessPath points to the binary inside the bundle (Contents/MacOS/Downio).
            // LaunchAgents usually run the binary directly.

            var plistContent = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.Downio.app</string>
    <key>ProgramArguments</key>
    <array>
        <string>{exePath}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>";
            File.WriteAllText(plistPath, plistContent);
        }
        else
        {
            if (File.Exists(plistPath))
            {
                File.Delete(plistPath);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private void SetWindowsAutoStart(bool enable)
    {
        try
        {
            // Using a shell command to avoid dependency on Microsoft.Win32.Registry if not available
            // But usually, we can use Registry.CurrentUser.OpenSubKey...
            // If the project doesn't have the reference, this might fail to compile.
            // Let's assume standard .NET 6+ Desktop includes it or we skip it for now.
            // Safe fallback: simple reg command
            
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            if (enable)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = $"add HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run /v {AppName} /t REG_SZ /d \"{exePath}\" /f",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = $"delete HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run /v {AppName} /f",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set windows autostart: {ex.Message}");
        }
    }
}
