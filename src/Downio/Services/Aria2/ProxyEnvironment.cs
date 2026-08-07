using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Downio.Services.Aria2;

internal static class ProxyEnvironment
{
    private static readonly string[] VariableNames =
    {
        "http_proxy",
        "https_proxy",
        "ftp_proxy",
        "all_proxy",
        "no_proxy"
    };

    private static bool _observedLaunchctlEnvironment;

    public static Dictionary<string, string> GetAria2Options()
    {
        var environment = GetCurrentProxyEnvironment();
        var options = new Dictionary<string, string>();
        foreach (var prefix in new[] { "http", "https", "ftp", "all" })
        {
            var proxy = GetValue(environment, $"{prefix}_proxy");
            var (user, password) = GetCredentials(proxy);
            options[$"{prefix}-proxy"] = proxy;
            options[$"{prefix}-proxy-user"] = user;
            options[$"{prefix}-proxy-passwd"] = password;
        }

        options["no-proxy"] = GetValue(environment, "no_proxy");

        return options;
    }

    private static (string User, string Password) GetCredentials(string proxy)
    {
        if (string.IsNullOrWhiteSpace(proxy)) return (string.Empty, string.Empty);

        var value = proxy.Contains("://", StringComparison.Ordinal) ? proxy : $"http://{proxy}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
        {
            return (string.Empty, string.Empty);
        }

        var separator = uri.UserInfo.IndexOf(':');
        if (separator < 0)
        {
            return (Unescape(uri.UserInfo), string.Empty);
        }

        return (
            Unescape(uri.UserInfo[..separator]),
            Unescape(uri.UserInfo[(separator + 1)..]));
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static Dictionary<string, string> GetCurrentProxyEnvironment()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ReadWindowsEnvironment();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var launchctlEnvironment = ReadLaunchctlEnvironment();
            if (launchctlEnvironment.Count > 0)
            {
                _observedLaunchctlEnvironment = true;
            }

            if (_observedLaunchctlEnvironment)
            {
                return launchctlEnvironment;
            }
        }

        return ReadProcessEnvironment();
    }

    private static Dictionary<string, string> ReadWindowsEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in VariableNames)
        {
            var upperName = name.ToUpperInvariant();
            var value = Environment.GetEnvironmentVariable(upperName, EnvironmentVariableTarget.User)
                        ?? Environment.GetEnvironmentVariable(upperName, EnvironmentVariableTarget.Machine)
                        ?? Environment.GetEnvironmentVariable(upperName);
            if (value != null)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static Dictionary<string, string> ReadLaunchctlEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in VariableNames)
        {
            var value = ReadLaunchctlValue(name) ?? ReadLaunchctlValue(name.ToUpperInvariant());
            if (value != null)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static string? ReadLaunchctlValue(string name)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/bin/launchctl",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("getenv");
            startInfo.ArgumentList.Add(name);

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var value = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(1000) || process.ExitCode != 0 || string.IsNullOrEmpty(value))
            {
                return null;
            }

            return value;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string> ReadProcessEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in VariableNames)
        {
            var value = Environment.GetEnvironmentVariable(name)
                        ?? Environment.GetEnvironmentVariable(name.ToUpperInvariant());
            if (value != null)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> environment, string name)
    {
        return environment.TryGetValue(name, out var value) ? value : string.Empty;
    }
}
