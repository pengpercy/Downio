using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Downio.Services;

/// <summary>
/// Turns a downloaded release asset into something ready to swap in, then performs the swap.
/// <see cref="PrepareAsync"/> runs inside the app (the "Extracting update" step, with progress);
/// <see cref="TryInstallAndRelaunch"/> hands the final file swap to a small script that waits
/// for this process to exit, replaces the installation and starts the new version.
/// </summary>
public static class UpdateApplier
{
    private static string UpdaterDirectory => Path.Combine(Path.GetTempPath(), "Downio", "updater");

    /// <summary>
    /// Extracts the download and returns the staged update: the new app folder on Windows,
    /// the new .app bundle on macOS, or the executable AppImage on Linux.
    /// </summary>
    public static async Task<string> PrepareAsync(string downloadedFilePath, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var stagingDir = Path.Combine(UpdaterDirectory, "staged");
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
        Directory.CreateDirectory(stagingDir);

        if (OperatingSystem.IsLinux())
        {
            var appImage = Path.Combine(stagingDir, Path.GetFileName(downloadedFilePath));
            File.Copy(downloadedFilePath, appImage, true);
            File.SetUnixFileMode(appImage, File.GetUnixFileMode(appImage) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            progress.Report(1);
            return appImage;
        }

        if (downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ExtractZip(downloadedFilePath, stagingDir, progress, cancellationToken), cancellationToken);
        }
        else if (OperatingSystem.IsMacOS() && downloadedFilePath.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
        {
            await CopyAppFromDmgAsync(downloadedFilePath, stagingDir, cancellationToken);
            progress.Report(1);
        }
        else
        {
            throw new NotSupportedException($"Unsupported update package: {Path.GetFileName(downloadedFilePath)}");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Directory.EnumerateDirectories(stagingDir, "*.app", SearchOption.AllDirectories).FirstOrDefault()
                   ?? throw new InvalidDataException("The update package contains no .app bundle.");
        }

        // Release zips may wrap everything in one top-level folder.
        var entries = Directory.EnumerateFileSystemEntries(stagingDir).ToList();
        return entries.Count == 1 && Directory.Exists(entries[0]) ? entries[0] : stagingDir;
    }

    public static bool TryInstallAndRelaunch(string stagedPath)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return InstallWindows(stagedPath);
            if (OperatingSystem.IsMacOS()) return InstallMac(stagedPath);
            return InstallLinux(stagedPath);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Starting the update installer failed");
            return false;
        }
    }

    // Entry by entry so progress can be reported, restoring Unix permissions and symlinks from the zip metadata.
    private static void ExtractZip(string zipPath, string destination, IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var total = Math.Max(1, archive.Entries.Count);
        var done = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidDataException($"Unsafe path in update package: {entry.FullName}");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
                const int symlinkType = 0xA000;
                if (!OperatingSystem.IsWindows() && (unixMode & 0xF000) == symlinkType)
                {
                    using var reader = new StreamReader(entry.Open());
                    File.CreateSymbolicLink(target, reader.ReadToEnd());
                }
                else
                {
                    entry.ExtractToFile(target, true);
                    if (!OperatingSystem.IsWindows() && (unixMode & 0x1FF) != 0)
                        File.SetUnixFileMode(target, (UnixFileMode)(unixMode & 0x1FF));
                }
            }

            progress.Report(++done / (double)total);
        }
    }

    private static async Task CopyAppFromDmgAsync(string dmgPath, string destination, CancellationToken cancellationToken)
    {
        var mountPoint = Path.Combine(UpdaterDirectory, "mount");
        Directory.CreateDirectory(mountPoint);
        await RunAsync("/usr/bin/hdiutil", cancellationToken, "attach", "-nobrowse", "-readonly", "-mountpoint", mountPoint, dmgPath);
        try
        {
            var app = Directory.EnumerateDirectories(mountPoint, "*.app").FirstOrDefault()
                      ?? throw new InvalidDataException("The disk image contains no .app bundle.");
            await RunAsync("/usr/bin/ditto", cancellationToken, app, Path.Combine(destination, Path.GetFileName(app)));
        }
        finally
        {
            await RunAsync("/usr/bin/hdiutil", CancellationToken.None, "detach", mountPoint, "-force");
        }
    }

    private static async Task RunAsync(string fileName, CancellationToken cancellationToken, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"{fileName} could not be started.");
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new IOException($"{Path.GetFileName(fileName)} exited with code {process.ExitCode}: {error.Trim()}");
    }

    private static bool InstallWindows(string stagedDir)
    {
        var processPath = Environment.ProcessPath;
        var targetDir = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(targetDir)) return false;

        var scriptPath = Path.Combine(UpdaterDirectory, "apply_update.bat");
        var bat = new StringBuilder();
        bat.AppendLine("@echo off");
        bat.AppendLine($"set PID={Environment.ProcessId}");
        bat.AppendLine($"set \"SOURCE={EscapeForBatValue(stagedDir)}\"");
        bat.AppendLine($"set \"TARGET={EscapeForBatValue(targetDir)}\"");
        bat.AppendLine($"set \"RESTART={EscapeForBatValue(processPath)}\"");
        bat.AppendLine(":wait");
        bat.AppendLine("tasklist /fi \"PID eq %PID%\" | find \"%PID%\" >nul");
        bat.AppendLine("if \"%ERRORLEVEL%\"==\"0\" (");
        bat.AppendLine("  timeout /t 1 /nobreak >nul");
        bat.AppendLine("  goto wait");
        bat.AppendLine(")");
        // Mirror the new build over the old one; files that no longer ship are left alone.
        bat.AppendLine("robocopy \"%SOURCE%\" \"%TARGET%\" /E /R:3 /W:1 /NFL /NDL /NJH /NJS /NP");
        // Builds before 1.0.92 kept their data under Assets\; it now lives in resources\.
        bat.AppendLine("if exist \"%SOURCE%\\resources\" if exist \"%TARGET%\\Assets\" rmdir /s /q \"%TARGET%\\Assets\"");
        bat.AppendLine("start \"\" \"%RESTART%\"");
        File.WriteAllText(scriptPath, bat.ToString(), new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = UpdaterDirectory
        });
        return true;
    }

    private static bool InstallMac(string stagedApp)
    {
        var appBundlePath = FindAppBundlePath(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(appBundlePath)) return false;

        var scriptPath = Path.Combine(UpdaterDirectory, "apply_update.sh");
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine($"PID={Environment.ProcessId}");
        sb.AppendLine($"NEW_APP='{QuoteForSh(stagedApp)}'");
        sb.AppendLine($"TARGET_APP='{QuoteForSh(appBundlePath)}'");
        sb.AppendLine("BACKUP_APP=\"$TARGET_APP.bak\"");
        sb.AppendLine("while kill -0 \"$PID\" 2>/dev/null; do sleep 0.5; done");
        sb.AppendLine("rm -rf \"$BACKUP_APP\"");
        sb.AppendLine("mv \"$TARGET_APP\" \"$BACKUP_APP\" || exit 1");
        // Put the old bundle back if the copy fails, so a failed update never leaves the app missing.
        sb.AppendLine("if ! ditto \"$NEW_APP\" \"$TARGET_APP\"; then rm -rf \"$TARGET_APP\"; mv \"$BACKUP_APP\" \"$TARGET_APP\"; open \"$TARGET_APP\"; exit 1; fi");
        sb.AppendLine("xattr -dr com.apple.quarantine \"$TARGET_APP\" >/dev/null 2>&1");
        sb.AppendLine("rm -rf \"$BACKUP_APP\"");
        sb.AppendLine("open \"$TARGET_APP\"");
        File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList = { scriptPath },
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = UpdaterDirectory
        });
        return true;
    }

    private static bool InstallLinux(string stagedAppImage)
    {
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (string.IsNullOrWhiteSpace(appImagePath)) return false;

        var scriptPath = Path.Combine(UpdaterDirectory, "apply_update.sh");
        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine($"PID={Environment.ProcessId}");
        sb.AppendLine($"NEW_APPIMAGE='{QuoteForSh(stagedAppImage)}'");
        sb.AppendLine($"TARGET_APPIMAGE='{QuoteForSh(appImagePath)}'");
        sb.AppendLine("while kill -0 \"$PID\" 2>/dev/null; do sleep 0.5; done");
        sb.AppendLine("mv -f \"$NEW_APPIMAGE\" \"$TARGET_APPIMAGE\" || exit 1");
        sb.AppendLine("chmod +x \"$TARGET_APPIMAGE\"");
        sb.AppendLine("\"$TARGET_APPIMAGE\" >/dev/null 2>&1 &");
        File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList = { scriptPath },
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = UpdaterDirectory
        });
        return true;
    }

    private static string? FindAppBundlePath(string? processPath)
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

    private static string EscapeForBatValue(string value) => value.Replace("\"", "\"\"");

    private static string QuoteForSh(string path) => path.Replace("'", "'\"'\"'");
}
