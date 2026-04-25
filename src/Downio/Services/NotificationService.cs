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

    public static void InitializePlatformIntegration()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if WINDOWS
            WindowsSystemNotification.Initialize();
#endif
        }
    }

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
        try
        {
#if WINDOWS
            WindowsSystemNotification.Show(title, message);
#endif
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to show Windows notification");
        }
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
