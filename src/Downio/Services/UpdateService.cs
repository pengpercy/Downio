using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Downio.Models;

namespace Downio.Services;

public class UpdateService
{
    private const string GitHubReleasesApiUrl = "https://api.github.com/repos/pengpercy/Downio/releases";
    private readonly HttpClient _httpClient;
    private readonly UpdateChannel _updateChannel;
    private readonly string _skipVersion;

    public UpdateService(AppSettings? settings = null)
    {
        _updateChannel = settings?.UpdateChannel ?? UpdateChannel.Stable;
        _skipVersion = settings?.SkipVersion?.Trim() ?? string.Empty;
        var handler = new HttpClientHandler();

        if (settings != null)
        {
            var address = settings.ProxyAddress?.Trim() ?? string.Empty;
            var port = settings.ProxyPort;
            if (!string.IsNullOrWhiteSpace(address) && port > 0)
            {
                var scheme = string.Equals(settings.ProxyType, "SOCKS5", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
                var proxyUri = new Uri($"{scheme}://{address}:{port}");
                var proxy = new WebProxy(proxyUri);

                var user = settings.ProxyUsername?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(user))
                {
                    proxy.Credentials = new NetworkCredential(user, settings.ProxyPassword ?? string.Empty);
                }

                handler.UseProxy = true;
                handler.Proxy = proxy;
            }
        }

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Downio");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    public async Task<ReleaseInfo?> CheckForUpdatesAsync(string currentVersion)
    {
        try
        {
            if (!SemanticVersion.TryParse(currentVersion, out var installedVersion))
            {
                throw new FormatException($"Invalid current version: {currentVersion}");
            }

            var release = await GetLatestReleaseCoreAsync();

            if (release != null &&
                SemanticVersion.TryParse(release.TagName, out var releaseVersion) &&
                releaseVersion.CompareTo(installedVersion) > 0)
            {
                return release;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
            throw;
        }
        return null;
    }

    public async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        try
        {
            return await GetLatestReleaseCoreAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Get latest release failed: {ex.Message}");
            return null;
        }
    }

    private async Task<ReleaseInfo?> GetLatestReleaseCoreAsync()
    {
        var response = await _httpClient.GetAsync(GitHubReleasesApiUrl);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var releases = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.ListReleaseInfo) ?? new List<ReleaseInfo>();

        return releases
            .Where(IsReleaseAllowed)
            .Select(r => new { Release = r, Parsed = SemanticVersion.TryParse(r.TagName, out var version) ? version : null })
            .Where(item => item.Parsed is not null)
            .OrderByDescending(item => item.Parsed)
            .Select(item => item.Release)
            .FirstOrDefault();
    }

    private bool IsReleaseAllowed(ReleaseInfo release)
    {
        if (release.Draft ||
            (_updateChannel == UpdateChannel.Stable && release.Prerelease) ||
            string.Equals(release.TagName, _skipVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public async Task DownloadUpdateAsync(string downloadUrl, string destinationPath, IProgress<double> progress)
    {
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var canReportProgress = totalBytes != -1;

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;

            if (canReportProgress)
            {
                progress.Report((double)totalRead / totalBytes);
            }
        }
    }
}

internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, int revision, string[] prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Revision = revision;
        Prerelease = prerelease;
    }

    private int Major { get; }
    private int Minor { get; }
    private int Patch { get; }
    private int Revision { get; }
    private string[] Prerelease { get; }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().TrimStart('v', 'V').Split('+', 2)[0];
        var parts = normalized.Split('-', 2);
        var core = parts[0].Split('.');
        if (core.Length is < 1 or > 4) return false;

        var numbers = new int[4];
        for (var i = 0; i < core.Length; i++)
        {
            if (!int.TryParse(core[i], out numbers[i]) || numbers[i] < 0) return false;
        }

        var prerelease = parts.Length == 2
            ? parts[1].Split('.', StringSplitOptions.RemoveEmptyEntries)
            : [];
        if (parts.Length == 2 && prerelease.Length == 0) return false;

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], numbers[3], prerelease);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;

        var coreComparison = Major.CompareTo(other.Major);
        if (coreComparison == 0) coreComparison = Minor.CompareTo(other.Minor);
        if (coreComparison == 0) coreComparison = Patch.CompareTo(other.Patch);
        if (coreComparison == 0) coreComparison = Revision.CompareTo(other.Revision);
        if (coreComparison != 0) return coreComparison;

        if (Prerelease.Length == 0) return other.Prerelease.Length == 0 ? 0 : 1;
        if (other.Prerelease.Length == 0) return -1;

        var length = Math.Min(Prerelease.Length, other.Prerelease.Length);
        for (var i = 0; i < length; i++)
        {
            var leftNumeric = int.TryParse(Prerelease[i], out var leftNumber);
            var rightNumeric = int.TryParse(other.Prerelease[i], out var rightNumber);

            int comparison;
            if (leftNumeric && rightNumeric)
            {
                comparison = leftNumber.CompareTo(rightNumber);
            }
            else if (leftNumeric)
            {
                comparison = -1;
            }
            else if (rightNumeric)
            {
                comparison = 1;
            }
            else
            {
                comparison = string.Compare(Prerelease[i], other.Prerelease[i], StringComparison.Ordinal);
            }

            if (comparison != 0) return comparison;
        }

        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }
}

public class ReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("assets")]
    public List<ReleaseAsset> Assets { get; set; } = new();
}

public class ReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReleaseInfo))]
[JsonSerializable(typeof(List<ReleaseInfo>))]
public partial class GitHubJsonContext : JsonSerializerContext
{
}
