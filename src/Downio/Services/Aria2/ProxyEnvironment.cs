using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

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

    private static readonly string[] MacShellProfiles =
    {
        ".zshrc", ".zshenv", ".zprofile",
        ".bash_profile", ".bashrc", ".profile"
    };

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

    public static HttpClientHandler CreateHttpHandler(
        string? appProxyType = null,
        string? appProxyAddress = null,
        int appProxyPort = 0,
        string? appProxyUsername = null,
        string? appProxyPassword = null,
        string? taskProxy = null)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true
        };

        if (!string.IsNullOrWhiteSpace(taskProxy) && Uri.TryCreate(taskProxy, UriKind.Absolute, out var taskUri))
        {
            handler.UseProxy = true;
            handler.Proxy = new WebProxy(taskUri);
            return handler;
        }

        var address = appProxyAddress?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(address) && appProxyPort > 0)
        {
            var scheme = string.Equals(appProxyType, "SOCKS5", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
            var proxyUri = new Uri($"{scheme}://{address}:{appProxyPort}");
            var proxy = new WebProxy(proxyUri);
            var user = appProxyUsername?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(user))
            {
                proxy.Credentials = new NetworkCredential(user, appProxyPassword ?? string.Empty);
            }
            handler.UseProxy = true;
            handler.Proxy = proxy;
            return handler;
        }

        var environment = GetCurrentProxyEnvironment();
        var envProxy = GetValue(environment, "https_proxy");
        if (string.IsNullOrWhiteSpace(envProxy))
        {
            envProxy = GetValue(environment, "http_proxy");
        }
        if (string.IsNullOrWhiteSpace(envProxy))
        {
            envProxy = GetValue(environment, "all_proxy");
        }

        if (!string.IsNullOrWhiteSpace(envProxy))
        {
            var normalized = envProxy.Contains("://", StringComparison.Ordinal) ? envProxy : $"http://{envProxy}";
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var envUri))
            {
                var webProxy = new WebProxy(envUri);
                var (user, password) = GetCredentials(envProxy);
                if (!string.IsNullOrEmpty(user))
                {
                    webProxy.Credentials = new NetworkCredential(user, password);
                }
                handler.UseProxy = true;
                handler.Proxy = webProxy;
            }
        }

        return handler;
    }

    public static void ApplyTo(ProcessStartInfo startInfo)
    {
        var environment = GetCurrentProxyEnvironment();
        foreach (var name in VariableNames)
        {
            if (environment.TryGetValue(name, out var value))
            {
                startInfo.Environment[name] = value;
            }
        }
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
            var shellProfile = ReadShellProfileEnvironment();
            var launchctl = ReadLaunchctlEnvironment();
            var process = ReadProcessEnvironment();
            var baseEnv = MergeEnvironments(shellProfile, launchctl);
            return MergeEnvironments(baseEnv, process);
        }

        return MergeEnvironments(ReadShellProfileEnvironment(), ReadProcessEnvironment());
    }

    private static Dictionary<string, string> ReadWindowsEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in VariableNames)
        {
            var value = GetWindowsEnvironmentVariable(name, EnvironmentVariableTarget.User)
                        ?? GetWindowsEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
                        ?? Environment.GetEnvironmentVariable(name)
                        ?? Environment.GetEnvironmentVariable(name.ToUpperInvariant());
            if (value != null)
            {
                result[name] = value;
            }
        }

        return result;
    }

    private static string? GetWindowsEnvironmentVariable(string name, EnvironmentVariableTarget target)
    {
        return Environment.GetEnvironmentVariable(name, target)
               ?? Environment.GetEnvironmentVariable(name.ToUpperInvariant(), target);
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

    private static Dictionary<string, string> ReadShellProfileEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) return result;

        foreach (var profile in MacShellProfiles)
        {
            var path = Path.Combine(home, profile);
            if (!File.Exists(path)) continue;

            try
            {
                foreach (var rawLine in File.ReadLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    var exportMatch = Regex.Match(
                        line,
                        @"^export\s+(?:--\s+)?(\w+)=[""']?(.+?)[""']?\s*(?:#.*)?$");
                    if (!exportMatch.Success) continue;

                    var varName = exportMatch.Groups[1].Value;
                    var varValue = exportMatch.Groups[2].Value;

                    foreach (var proxyName in VariableNames)
                    {
                        if (result.ContainsKey(proxyName)) continue;
                        if (string.Equals(varName, proxyName, StringComparison.Ordinal) ||
                            string.Equals(varName, proxyName.ToUpperInvariant(), StringComparison.Ordinal))
                        {
                            result[proxyName] = varValue;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        return result;
    }

    private static Dictionary<string, string> MergeEnvironments(
        IReadOnlyDictionary<string, string> defaults,
        IReadOnlyDictionary<string, string> overrides)
    {
        var result = new Dictionary<string, string>(defaults, StringComparer.Ordinal);
        foreach (var pair in overrides)
        {
            result[pair.Key] = pair.Value;
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
