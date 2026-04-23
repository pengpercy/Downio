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
    private const string GitHubApiUrl = "https://api.github.com/repos/pengpercy/Downio/releases/latest";
    private readonly HttpClient _httpClient;

    public UpdateService(AppSettings? settings = null)
    {
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
            // In AOT, we need to be careful with JSON.
            // For now, let's use a source generated context if possible or standard deserialize if not AOT-strict strict on this part.
            // But since we are AOT, we should use source generator.
            // Let's create a simple context for ReleaseInfo.
            
            var response = await _httpClient.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.ReleaseInfo);

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
            var response = await _httpClient.GetAsync(GitHubApiUrl);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.ReleaseInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Get latest release failed: {ex.Message}");
            return null;
        }
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

public class ReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

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
public partial class GitHubJsonContext : JsonSerializerContext
{
}
