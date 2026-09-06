using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
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
            var systemProxy = ReadMacSystemProxyEnvironment();
            var shellProfile = ReadShellProfileEnvironment();
            var launchctl = ReadLaunchctlEnvironment();
            var process = ReadProcessEnvironment();
            // The OS-level system proxy (Clash / Surge ...) is the
            // lowest-priority fallback: explicit environment variables
            // always win when they are present.
            var baseEnv = MergeEnvironments(systemProxy, shellProfile);
            baseEnv = MergeEnvironments(baseEnv, launchctl);
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

        // WinINET system proxy (set by Clash for Windows / V2rayN ...) is
        // the lowest-priority fallback behind explicit environment
        // variables.
        return MergeEnvironments(ReadWindowsSystemProxyEnvironment(), result);
    }

    private static string? GetWindowsEnvironmentVariable(string name, EnvironmentVariableTarget target)
    {
        return Environment.GetEnvironmentVariable(name, target)
               ?? Environment.GetEnvironmentVariable(name.ToUpperInvariant(), target);
    }

    // ------------------------------------------------------------------
    // OS system proxy fallback (Clash / Surge / V2rayN ...)
    //
    // Proxy tools enable a system-wide proxy through the OS settings
    // store (macOS SystemConfiguration, Windows WinINET). They do NOT
    // export http_proxy / https_proxy / ... into process or user
    // environment variables, so a GUI app never sees them unless it
    // reads the OS settings directly. These readers turn the OS system
    // proxy into the same shape as the environment variables and act
    // as the lowest-priority fallback.
    // ------------------------------------------------------------------

    private static readonly object SystemProxySync = new();
    private static Dictionary<string, string>? _cachedMacSystemProxy;
    private static DateTime _macSystemProxyReadAtUtc;
    private static readonly TimeSpan SystemProxyCacheTtl = TimeSpan.FromSeconds(5);
    private static string? _lastLoggedSystemProxy;

    private static Dictionary<string, string> ReadMacSystemProxyEnvironment()
    {
        lock (SystemProxySync)
        {
            if (_cachedMacSystemProxy is not null &&
                DateTime.UtcNow - _macSystemProxyReadAtUtc < SystemProxyCacheTtl)
            {
                return _cachedMacSystemProxy;
            }
        }

        var result = ParseScutilProxyOutput(RunCapture("/usr/sbin/scutil", "--proxy") ?? string.Empty);
        LogSystemProxyOnce("macOS system proxy", result);

        lock (SystemProxySync)
        {
            _cachedMacSystemProxy = result;
            _macSystemProxyReadAtUtc = DateTime.UtcNow;
        }

        return result;
    }

    internal static Dictionary<string, string> ParseScutilProxyOutput(string output)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(output)) return result;

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        var exceptions = new List<string>();
        var inExceptionsArray = false;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (inExceptionsArray)
            {
                if (line.StartsWith('}'))
                {
                    inExceptionsArray = false;
                }
                else
                {
                    var separator = line.IndexOf(':');
                    var entry = separator >= 0 ? line[(separator + 1)..].Trim() : string.Empty;
                    if (entry.Length > 0) exceptions.Add(entry);
                }

                continue;
            }

            var match = Regex.Match(line, @"^([A-Za-z]+)\s*:\s*(.+)$");
            if (!match.Success) continue;

            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim();
            if (key == "ExceptionsList")
            {
                inExceptionsArray = value.StartsWith("<array", StringComparison.Ordinal);
                continue;
            }

            settings[key] = value;
        }

        string? BuildProxy(string enableKey, string hostKey, string portKey, string scheme)
        {
            if (!settings.TryGetValue(enableKey, out var enabled) || enabled != "1") return null;
            if (!settings.TryGetValue(hostKey, out var host) || string.IsNullOrWhiteSpace(host)) return null;
            if (!settings.TryGetValue(portKey, out var port) ||
                !int.TryParse(port, out var portValue) || portValue <= 0) return null;
            return $"{scheme}://{host}:{portValue}";
        }

        var httpProxy = BuildProxy("HTTPEnable", "HTTPProxy", "HTTPPort", "http");
        var httpsProxy = BuildProxy("HTTPSEnable", "HTTPSProxy", "HTTPSPort", "http");
        var socksProxy = BuildProxy("SOCKSEnable", "SOCKSProxy", "SOCKSPort", "socks5");

        if (httpProxy != null) result["http_proxy"] = httpProxy;
        if (httpsProxy != null) result["https_proxy"] = httpsProxy;

        // all_proxy covers the remaining protocols (ftp, ...). Prefer the
        // HTTP proxy and only fall back to SOCKS when neither HTTP nor
        // HTTPS system proxy is enabled.
        if (httpProxy != null)
        {
            result["all_proxy"] = httpProxy;
        }
        else if (httpsProxy != null)
        {
            result["all_proxy"] = httpsProxy;
        }
        else if (socksProxy != null)
        {
            result["all_proxy"] = socksProxy;
        }

        if (exceptions.Count > 0)
        {
            result["no_proxy"] = string.Join(",", exceptions
                .Select(entry => entry.StartsWith("*.", StringComparison.Ordinal) ? entry[1..] : entry));
        }

        return result;
    }

    private static Dictionary<string, string> ReadWindowsSystemProxyEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return result;

        try
        {
            var enabled = RegistryGetDword(HkeyCurrentUser, InternetSettingsSubKey, "ProxyEnable");
            var server = RegistryGetString(HkeyCurrentUser, InternetSettingsSubKey, "ProxyServer");
            if (enabled != 1 || string.IsNullOrWhiteSpace(server)) return result;

            string? httpProxy = null, httpsProxy = null, ftpProxy = null, socksProxy = null;
            if (server.Contains('=', StringComparison.Ordinal))
            {
                foreach (var part in server.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var separator = part.IndexOf('=');
                    if (separator <= 0) continue;

                    var scheme = part[..separator];
                    var target = part[(separator + 1)..];
                    if (target.Length == 0) continue;

                    switch (scheme.ToLowerInvariant())
                    {
                        case "http": httpProxy = target; break;
                        case "https": httpsProxy = target; break;
                        case "ftp": ftpProxy = target; break;
                        case "socks": socksProxy = target; break;
                    }
                }
            }
            else
            {
                httpProxy = httpsProxy = ftpProxy = server;
            }

            string? Normalize(string? target)
            {
                if (string.IsNullOrWhiteSpace(target)) return null;
                return target.Contains("://", StringComparison.Ordinal) ? target : $"http://{target}";
            }

            httpProxy = Normalize(httpProxy);
            httpsProxy = Normalize(httpsProxy);
            ftpProxy = Normalize(ftpProxy);
            if (!string.IsNullOrWhiteSpace(socksProxy) &&
                !socksProxy.Contains("://", StringComparison.Ordinal))
            {
                socksProxy = $"socks5://{socksProxy}";
            }

            if (httpProxy != null) result["http_proxy"] = httpProxy;
            if (httpsProxy != null) result["https_proxy"] = httpsProxy;
            if (ftpProxy != null) result["ftp_proxy"] = ftpProxy;
            if (httpProxy != null)
            {
                result["all_proxy"] = httpProxy;
            }
            else if (httpsProxy != null)
            {
                result["all_proxy"] = httpsProxy;
            }
            else if (ftpProxy != null)
            {
                result["all_proxy"] = ftpProxy;
            }
            else if (!string.IsNullOrWhiteSpace(socksProxy))
            {
                result["all_proxy"] = socksProxy;
            }

            var overrideList = RegistryGetString(HkeyCurrentUser, InternetSettingsSubKey, "ProxyOverride");
            if (!string.IsNullOrWhiteSpace(overrideList))
            {
                var entries = overrideList
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(entry => !string.Equals(entry, "<local>", StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.StartsWith("*.", StringComparison.Ordinal) ? entry[1..] : entry)
                    .ToList();
                if (entries.Count > 0)
                {
                    result["no_proxy"] = string.Join(",", entries);
                }
            }
        }
        catch
        {
            // System proxy is best-effort; ignore registry read failures.
        }

        LogSystemProxyOnce("Windows system proxy", result);
        return result;
    }

    private static void LogSystemProxyOnce(string source, IReadOnlyDictionary<string, string> proxy)
    {
        var hasProxy = proxy.ContainsKey("http_proxy") ||
                       proxy.ContainsKey("https_proxy") ||
                       proxy.ContainsKey("all_proxy");
        var description = hasProxy
            ? string.Join(", ", proxy.Select(pair => $"{pair.Key}={pair.Value}"))
            : "(none)";

        lock (SystemProxySync)
        {
            if (string.Equals(_lastLoggedSystemProxy, description, StringComparison.Ordinal)) return;
            _lastLoggedSystemProxy = description;
        }

        AppLog.Info($"Detected {source}: {description}");
    }

    private static string? RunCapture(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var value = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(2000) || process.ExitCode != 0)
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

    private const uint HkeyCurrentUserValue = 0x80000001u;
    private const string InternetSettingsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const uint RrfRtRegSz = 0x00000002;
    private const uint RrfRtRegDword = 0x00000010;

    private static readonly UIntPtr HkeyCurrentUser = new(HkeyCurrentUserValue);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegGetValueW(
        UIntPtr hKey,
        string lpSubKey,
        string lpValue,
        uint dwFlags,
        out uint pdwType,
        byte[] pvData,
        ref uint pcbData);

    private static string? RegistryGetString(UIntPtr root, string subKey, string value)
    {
        var data = new byte[512];
        var size = (uint)data.Length;
        var status = RegGetValueW(root, subKey, value, RrfRtRegSz, out _, data, ref size);
        if (status != 0 || size < 2) return null;

        return Encoding.Unicode.GetString(data, 0, (int)size).TrimEnd('\0');
    }

    private static int? RegistryGetDword(UIntPtr root, string subKey, string value)
    {
        var data = new byte[4];
        var size = (uint)data.Length;
        var status = RegGetValueW(root, subKey, value, RrfRtRegDword, out _, data, ref size);
        if (status != 0 || size < 4) return null;

        return BitConverter.ToInt32(data, 0);
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
