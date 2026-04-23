using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Downio.Services;

public static class UpdateApplier
{
    public static async Task<bool> TryApplyUpdateAsync(string downloadedFilePath, string releaseVersion)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await TryApplyWindowsZipAsync(downloadedFilePath);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await TryApplyMacZipAsync(downloadedFilePath);
        }

        return await TryApplyLinuxAppImageAsync(downloadedFilePath, releaseVersion);
    }

    private static Task<bool> TryApplyWindowsZipAsync(string zipPath)
    {
        if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return Task.FromResult(false);
        }

        var targetDir = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            return Task.FromResult(false);
        }

        var updaterDir = Path.Combine(Path.GetTempPath(), "Downio", "updater");
        Directory.CreateDirectory(updaterDir);

        var extractedDir = Path.Combine(updaterDir, "extracted");
        var scriptPath = Path.Combine(updaterDir, "apply_update.bat");
        var pid = Environment.ProcessId;

        if (Directory.Exists(extractedDir))
        {
            try { Directory.Delete(extractedDir, true); } catch { }
        }

        var exeName = Path.GetFileName(processPath);
        var restartExe = Path.Combine(targetDir, exeName);

        var bat = new StringBuilder();
        bat.AppendLine("@echo off");
        bat.AppendLine("setlocal enabledelayedexpansion");
        bat.AppendLine($"set PID={pid}");
        bat.AppendLine($"set \"ZIP={EscapeForBatValue(zipPath)}\"");
        bat.AppendLine($"set \"EXTRACTED={EscapeForBatValue(extractedDir)}\"");
        bat.AppendLine($"set \"TARGET={EscapeForBatValue(targetDir)}\"");
        bat.AppendLine($"set \"RESTART={EscapeForBatValue(restartExe)}\"");
        bat.AppendLine(":wait");
        bat.AppendLine("tasklist /fi \"PID eq %PID%\" | find \"%PID%\" >nul");
        bat.AppendLine("if \"%ERRORLEVEL%\"==\"0\" (");
        bat.AppendLine("  timeout /t 1 /nobreak >nul");
        bat.AppendLine("  goto wait");
        bat.AppendLine(")");
        bat.AppendLine("rmdir /s /q \"%EXTRACTED%\" 2>nul");
        bat.AppendLine("mkdir \"%EXTRACTED%\"");
        bat.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '%ZIP%' -DestinationPath '%EXTRACTED%' -Force\"");
        bat.AppendLine("set \"SOURCE=%EXTRACTED%\"");
        bat.AppendLine("for /f %%F in ('dir /b /a-d \"%EXTRACTED%\" 2^>nul ^| find /c /v \"\"') do set FILECOUNT=%%F");
        bat.AppendLine("for /f %%D in ('dir /b /ad \"%EXTRACTED%\" 2^>nul ^| find /c /v \"\"') do set DIRCOUNT=%%D");
        bat.AppendLine("if \"%FILECOUNT%\"==\"0\" if \"%DIRCOUNT%\"==\"1\" (");
        bat.AppendLine("  for /f \"delims=\" %%D in ('dir /b /ad \"%EXTRACTED%\"') do set \"SOURCE=%EXTRACTED%\\%%D\"");
        bat.AppendLine(")");
        bat.AppendLine("robocopy \"%SOURCE%\" \"%TARGET%\" /E /NFL /NDL /NJH /NJS /NP");
        bat.AppendLine("start \"\" \"%RESTART%\"");
        File.WriteAllText(scriptPath, bat.ToString(), Encoding.UTF8);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = updaterDir
        });

        return Task.FromResult(true);
    }

    private static Task<bool> TryApplyMacZipAsync(string zipOrDmgPath)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return Task.FromResult(false);
        }

        var appBundlePath = FindAppBundlePath(processPath);
        if (string.IsNullOrWhiteSpace(appBundlePath))
        {
            return Task.FromResult(false);
        }

        var parentDir = Path.GetDirectoryName(appBundlePath);
        if (string.IsNullOrWhiteSpace(parentDir))
        {
            return Task.FromResult(false);
        }

        var updaterDir = Path.Combine(Path.GetTempPath(), "Downio", "updater");
        Directory.CreateDirectory(updaterDir);
        var scriptPath = Path.Combine(updaterDir, "apply_update.sh");
        var pid = Environment.ProcessId;

        var sb = new StringBuilder();
        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine("set -e");
        sb.AppendLine($"PID={pid}");
        sb.AppendLine($"ARCHIVE='{QuoteForSh(zipOrDmgPath)}'");
        sb.AppendLine($"TARGET_APP='{QuoteForSh(appBundlePath)}'");
        sb.AppendLine($"PARENT_DIR='{QuoteForSh(parentDir)}'");
        sb.AppendLine("while kill -0 \"$PID\" 2>/dev/null; do sleep 1; done");
        sb.AppendLine("TMPDIR=$(mktemp -d)");
        sb.AppendLine("cleanup() { rm -rf \"$TMPDIR\"; }");
        sb.AppendLine("trap cleanup EXIT");
        sb.AppendLine("if [[ \"$ARCHIVE\" == *.zip ]]; then");
        sb.AppendLine("  ditto -x -k \"$ARCHIVE\" \"$TMPDIR\"");
        sb.AppendLine("elif [[ \"$ARCHIVE\" == *.dmg ]]; then");
        sb.AppendLine("  MOUNT_OUTPUT=$(hdiutil attach -nobrowse -readonly \"$ARCHIVE\")");
        sb.AppendLine("  MOUNT_POINT=$(echo \"$MOUNT_OUTPUT\" | awk -F'\\t' '/\\/Volumes\\//{print $NF; exit}')");
        sb.AppendLine("  cp -R \"$MOUNT_POINT\"/*.app \"$TMPDIR\"/ || true");
        sb.AppendLine("  hdiutil detach \"$MOUNT_POINT\" || true");
        sb.AppendLine("else");
        sb.AppendLine("  exit 1");
        sb.AppendLine("fi");
        sb.AppendLine("NEW_APP=$(find \"$TMPDIR\" -maxdepth 2 -name '*.app' -print -quit)");
        sb.AppendLine("if [[ -z \"$NEW_APP\" ]]; then exit 1; fi");
        sb.AppendLine("BACKUP_APP=\"$TARGET_APP.bak\"");
        sb.AppendLine("rm -rf \"$BACKUP_APP\" || true");
        sb.AppendLine("mv \"$TARGET_APP\" \"$BACKUP_APP\"");
        sb.AppendLine("ditto \"$NEW_APP\" \"$TARGET_APP\"");
        sb.AppendLine("xattr -dr com.apple.quarantine \"$TARGET_APP\" >/dev/null 2>&1 || true");
        sb.AppendLine("open \"$TARGET_APP\"");
        File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(false));

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch
        {
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = updaterDir
        });

        return Task.FromResult(true);
    }

    private static Task<bool> TryApplyLinuxAppImageAsync(string downloadedPath, string releaseVersion)
    {
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(appImagePath))
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && processPath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
            {
                appImagePath = processPath;
            }
        }

        if (string.IsNullOrWhiteSpace(appImagePath))
        {
            return Task.FromResult(false);
        }

        if (!downloadedPath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(false);
        }

        var updaterDir = Path.Combine(Path.GetTempPath(), "Downio", "updater");
        Directory.CreateDirectory(updaterDir);
        var scriptPath = Path.Combine(updaterDir, "apply_update.sh");
        var pid = Environment.ProcessId;

        var sb = new StringBuilder();
        sb.AppendLine("#!/usr/bin/env bash");
        sb.AppendLine("set -e");
        sb.AppendLine($"PID={pid}");
        sb.AppendLine($"NEW_APPIMAGE='{QuoteForSh(downloadedPath)}'");
        sb.AppendLine($"TARGET_APPIMAGE='{QuoteForSh(appImagePath)}'");
        sb.AppendLine("while kill -0 \"$PID\" 2>/dev/null; do sleep 1; done");
        sb.AppendLine("chmod +x \"$NEW_APPIMAGE\" || true");
        sb.AppendLine("mv -f \"$NEW_APPIMAGE\" \"$TARGET_APPIMAGE\"");
        sb.AppendLine("chmod +x \"$TARGET_APPIMAGE\" || true");
        sb.AppendLine("\"$TARGET_APPIMAGE\" >/dev/null 2>&1 &");
        File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"\"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = updaterDir
        });

        return Task.FromResult(true);
    }

    private static string? FindAppBundlePath(string processPath)
    {
        var dir = Path.GetDirectoryName(processPath);
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(dir))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    private static string EscapeForBatValue(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    private static string QuoteForSh(string path)
    {
        return path.Replace("'", "'\"'\"'");
    }
}
