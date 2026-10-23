using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Downio.Models;
using Downio.Services.Aria2;

namespace Downio.Services;

public class UpdateService
{
    private const string GitHubReleasesApiUrl = "https://api.github.com/repos/pengpercy/Downio/releases";
    private readonly HttpClient _httpClient;

    public UpdateService(AppSettings? settings = null)
    {
        var handler = settings != null
            ? ProxyEnvironment.CreateHttpHandler(
                settings.ProxyType,
                settings.ProxyAddress,
                settings.ProxyPort,
                settings.ProxyUsername,
                settings.ProxyPassword)
            : ProxyEnvironment.CreateHttpHandler();

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
            var release = await GetLatestReleaseCoreAsync();

            if (release != null)
            {
                var serverVersion = release.TagName.TrimStart('v');
                if (Version.TryParse(serverVersion, out var sVer) && Version.TryParse(currentVersion, out var cVer))
                {
                    if (sVer > cVer)
                    {
                        return release;
                    }
                }
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
            .Where(r => !r.Draft && !r.Prerelease && Version.TryParse(r.TagName.TrimStart('v'), out _))
            .OrderByDescending(r => Version.Parse(r.TagName.TrimStart('v')))
            .FirstOrDefault();
    }

    /// <summary>
    /// Streams the asset to <paramref name="destinationPath"/>, reporting (bytes so far, total bytes or -1),
    /// and checks it against the SHA-256 digest GitHub publishes for release assets when there is one.
    /// </summary>
    public async Task DownloadUpdateAsync(ReleaseAsset asset, string destinationPath, IProgress<(long Received, long Total)> progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : -1L);

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                sha256.AppendData(buffer, 0, bytesRead);
                totalRead += bytesRead;
                progress.Report((totalRead, totalBytes));
            }
        }

        const string digestPrefix = "sha256:";
        if (asset.Digest?.StartsWith(digestPrefix, StringComparison.OrdinalIgnoreCase) == true)
        {
            var actual = Convert.ToHexStringLower(sha256.GetHashAndReset());
            if (!actual.Equals(asset.Digest[digestPrefix.Length..], StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destinationPath);
                throw new InvalidDataException($"Checksum mismatch for {asset.Name}.");
            }
        }
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

    [JsonPropertyName("size")]
    public long Size { get; set; }

    // "sha256:<hex>", published by GitHub for release assets.
    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReleaseInfo))]
[JsonSerializable(typeof(List<ReleaseInfo>))]
public partial class GitHubJsonContext : JsonSerializerContext
{
}
