using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Downio.Models;
using Downio.Services.Notifications;

namespace Downio.Services;

public class NotificationService
{
    public event EventHandler<ToastMessage>? ToastRequested;

    public void ShowNotification(string title, string message, ToastType type = ToastType.Info)
    {
        // Always show in-app Toast notification
        ToastRequested?.Invoke(this, new ToastMessage { Title = title, Message = message, Type = type });

        // Always show native system notification
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ShowMacNotification(title, message);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ShowWindowsNotification(title, message);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ShowLinuxNotification(title, message);
        }
        // Linux support can be added with notify-send
    }

    private bool IsMainWindowActive()
    {
        if (Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.IsActive == true;
        }

        return false;
    }

// --- Windows 混合实现 (自动适配 Win7 和 Win10+) ---
    private static void ShowWindowsNotification(string title, string message)
    {
        // 获取 Windows 主版本号
        var major = Environment.OSVersion.Version.Major;

        // Windows 10 (Major=10) 及以上使用现代 Toast
        // Windows 7/8 (Major=6) 使用传统气泡
        if (major >= 10)
        {
            SendWindows10Toast(title, message);
        }
        else
        {
            SendWindows7Balloon(title, message);
        }
    }

    private static void SendWindows7Balloon(string title, string message)
    {
        string psScript = $@"
$code = @'
using System;
using System.Windows.Forms;
using System.Drawing;
public class LegacyToast {{
    public static void Show(string title, string message) {{
        var icon = new NotifyIcon();
        icon.Icon = SystemIcons.Information; 
        icon.Visible = true;
        icon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
    }}
}}
'@
Add-Type -TypeDefinition $code -ReferencedAssemblies System.Windows.Forms, System.Drawing
[LegacyToast]::Show('{EscapeForScript(title)}', '{EscapeForScript(message)}')
Start-Sleep -s 5 
";
        RunProcess("powershell", $"-NoProfile -WindowStyle Hidden -Command \"{psScript}\"");
    }

    private static void SendWindows10Toast(string title, string message)
    {
        var appLogoPath = PathCombineSafe(AppContext.BaseDirectory, "Assets", "Branding", "app_icon.png");
        var appLogoUri = FileExists(appLogoPath) ? new Uri(appLogoPath).AbsoluteUri : string.Empty;

        string psScript = $@"
$ErrorActionPreference = 'Stop';
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null;

$title = '{EscapeForScript(title)}';
$message = '{EscapeForScript(message)}';
$appLogoUri = '{EscapeForScript(appLogoUri)}';

try {{
$xml = New-Object -TypeName Windows.Data.Xml.Dom.XmlDocument;
$xml.LoadXml('<toast><visual><binding template=""ToastGeneric""></binding></visual></toast>');

$binding = $xml.SelectSingleNode('/toast/visual/binding');

if ($appLogoUri -ne '') {{
  $img = $xml.CreateElement('image');
  $img.SetAttribute('placement', 'appLogoOverride') > $null;
  $img.SetAttribute('src', $appLogoUri) > $null;
  $img.SetAttribute('alt', 'Downio') > $null;
  $img.SetAttribute('hint-crop', 'circle') > $null;
  $binding.AppendChild($img) > $null;
}}

$t1 = $xml.CreateElement('text');
$t1.InnerText = $title;
$binding.AppendChild($t1) > $null;

$t2 = $xml.CreateElement('text');
$t2.InnerText = $message;
$binding.AppendChild($t2) > $null;

$toast = [Windows.UI.Notifications.ToastNotification]::new($xml);
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Downio').Show($toast);
exit 0;
}} catch {{
  Write-Error $_;
  exit 1;
}}
";
        var exitCode = RunProcessAndWait("powershell", $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Sta -Command \"{psScript}\"", 2000);
        if (exitCode == 0) return;

        string fallback = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null;
$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02);
$textNodes = $template.GetElementsByTagName('text');
$textNodes[0].AppendChild($template.CreateTextNode('{EscapeForScript(title)}')) > $null;
$textNodes[1].AppendChild($template.CreateTextNode('{EscapeForScript(message)}')) > $null;
$toast = [Windows.UI.Notifications.ToastNotification]::new($template);
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Downio').Show($toast);
";
        RunProcessAndWait("powershell", $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Sta -Command \"{fallback}\"", 2000);
    }

    // --- MacOS 实现 ---
    private static void ShowMacNotification(string title, string message)
    {
        if (MacSystemNotification.TryShow(title, message))
        {
            return;
        }

        string script = $"display notification {EscapeAppleScriptString(message)} with title {EscapeAppleScriptString(title)}";
        RunProcess("/usr/bin/osascript", $"-e {EscapeForArgs(script)}");
    }

    // --- Linux 实现 ---
    private static void ShowLinuxNotification(string title, string message)
    {
        RunProcess("notify-send", $"\"{EscapeForScript(title)}\" \"{EscapeForScript(message)}\"");
    }

    // --- 辅助方法 ---
    private static void RunProcess(string fileName, string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(info);
        // 对于 Win7 气泡，脚本里有 Sleep，所以这里如果不等，C# 主线程不受影响，PowerShell 在后台跑
        // 对于其他平台，命令很快结束
    }

    private static int RunProcessAndWait(string fileName, string arguments, int timeoutMs)
    {
        var info = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(info);
        if (process == null) return -1;

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return -1;
        }

        return process.ExitCode;
    }

    private static string EscapeForScript(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        // 简单的单引号/双引号转义，防止 PowerShell/Bash 语法错误
        // 这里主要针对 PowerShell 的单引号包裹逻辑进行转义
        return input.Replace("'", "''").Replace("\"", "\\\"");
    }

    private static string EscapeAppleScriptString(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string EscapeForArgs(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string PathCombineSafe(params string[] parts) =>
        System.IO.Path.Combine(parts);

    private static bool FileExists(string path) =>
        System.IO.File.Exists(path);
}
