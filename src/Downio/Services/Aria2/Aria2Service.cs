using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Downio.Models;

namespace Downio.Services.Aria2;

public class Aria2Service : IAria2Service, IDisposable
{
    private static readonly string[] Aria2ProxyEnvironmentVariables =
    {
        "http_proxy",
        "https_proxy",
        "ftp_proxy",
        "all_proxy",
        "no_proxy"
    };
    private Process? _aria2Process;
    private JsonRpcClient? _rpcClient;
    private int _rpcPort = 16800;
    private string _rpcSecret = "DownioSecret";
    private string _configDir = string.Empty;
    private readonly ConcurrentDictionary<string, int> _splitCache = new();
    private readonly ConcurrentDictionary<string, string> _ed2kSearchDirectories = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ConcurrentQueue<string> _stderrTail = new();
    private bool _usesApplicationProxy;
    private string _ed2kServerListPath = string.Empty;
    private string _ed2kNodeListPath = string.Empty;

    public async Task InitializeAsync(AppSettings settings)
    {
        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _rpcPort = settings.RpcPort;
            _rpcSecret = string.IsNullOrWhiteSpace(settings.RpcSecret) ? string.Empty : settings.RpcSecret;

            if (_aria2Process != null && !_aria2Process.HasExited && _rpcClient != null)
            {
                if (await IsRpcReadyAsync(_rpcClient).ConfigureAwait(false))
                {
                    return;
                }
            }

            await ShutdownAsync().ConfigureAwait(false);

            // 1. Setup Config Directory
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _configDir = Path.Combine(appData, "Downio");
            Directory.CreateDirectory(_configDir);

            var sessionFile = Path.Combine(_configDir, "aria2.session");
            if (!File.Exists(sessionFile))
            {
                File.WriteAllText(sessionFile, "");
            }

            var configFile = Path.Combine(_configDir, "aria2.conf");
            if (!File.Exists(configFile))
            {
                File.WriteAllText(configFile, "# Custom aria2 configurations\n");
            }

            var logFile = Path.Combine(_configDir, "aria2.log");
            PrepareEd2kBootstrapFiles();

            // 2. Locate Binary
            var binaryPath = GetBinaryPath();
            if (!File.Exists(binaryPath))
            {
                Debug.WriteLine($"Aria2 binary not found at: {binaryPath}");
                AppLog.Warn($"Aria2 binary not found at: {binaryPath}");
            }

            // Ensure executable permission on Linux/macOS
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    Process.Start("chmod", $"+x \"{binaryPath}\"")?.WaitForExit();
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex, $"Failed to chmod aria2 binary: {binaryPath}");
                }
            }

            // 3. Start Process
            var args = new List<string>
            {
                "--enable-rpc=true",
                $"--rpc-listen-port={_rpcPort}",
                "--rpc-allow-origin-all=true",
                "--rpc-listen-all=true",
                $"--save-session={sessionFile}",
                $"--input-file={sessionFile}",
                $"--conf-path={configFile}",
                $"--log={logFile}",
                "--log-level=warn",
                "--max-concurrent-downloads=5",
                "--max-connection-per-server=16",
                "--split=16",
                "--min-split-size=1M",
                "--continue=true",
                $"--listen-port={settings.BtListenPort}",
                $"--dht-listen-port={settings.DhtListenPort}"
            };
            if (!string.IsNullOrWhiteSpace(_ed2kServerListPath))
            {
                args.Add($"--ed2k-server-list={_ed2kServerListPath}");
            }
            if (!string.IsNullOrWhiteSpace(_ed2kNodeListPath))
            {
                args.Add($"--ed2k-node-list={_ed2kNodeListPath}");
            }
            args.Add($"--ed2k-listen-port={Math.Clamp(settings.Ed2kListenPort, 0, 65535)}");
            args.Add($"--ed2k-udp-listen-port={Math.Clamp(settings.Ed2kUdpListenPort, 0, 65535)}");
            args.Add($"--ed2k-upload-slots={Math.Clamp(settings.Ed2kUploadSlots, 1, 100)}");
            var ed2kServers = NormalizeEd2kServers(settings.Ed2kServer);
            if (!string.IsNullOrWhiteSpace(ed2kServers))
            {
                args.Add($"--ed2k-server={ed2kServers}");
            }
            if (!string.IsNullOrWhiteSpace(_rpcSecret))
            {
                args.Insert(2, $"--rpc-secret={_rpcSecret}");
            }

            if (!string.IsNullOrWhiteSpace(settings.GlobalUserAgent))
            {
                args.Add($"--user-agent={settings.GlobalUserAgent}");
            }
            else
            {
                args.Add("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.93 Safari/537.36");
            }

            if (!string.IsNullOrWhiteSpace(settings.BtTrackers))
            {
                var trackers = NormalizeBtTrackers(settings.BtTrackers);
                if (!string.IsNullOrWhiteSpace(trackers))
                {
                    args.Add($"--bt-tracker={trackers}");
                }
            }

            var caBundlePath = Path.Combine(GetContentRoot(), "Assets", "cacert.pem");
            if (File.Exists(caBundlePath))
            {
                args.Add($"--ca-certificate={caBundlePath}");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            AddAria2ProxyEnvironmentAliases(startInfo);

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            try
            {
                while (_stderrTail.TryDequeue(out var _)) { }
                _aria2Process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                _aria2Process.OutputDataReceived += (_, _) => { };
                _aria2Process.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    _stderrTail.Enqueue(e.Data);
                    while (_stderrTail.Count > 40)
                    {
                        _stderrTail.TryDequeue(out var _);
                    }
                    AppLog.Warn($"aria2: {e.Data}");
                };

                _aria2Process.Start();
                _aria2Process.BeginOutputReadLine();
                _aria2Process.BeginErrorReadLine();

                Debug.WriteLine($"Aria2 started. PID: {_aria2Process.Id}");
                AppLog.Info($"Aria2 started. PID: {_aria2Process.Id}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start aria2: {ex.Message}");
                AppLog.Error(ex, "Failed to start aria2");
                throw;
            }

            // 4. Init Client + Wait Ready
            _rpcClient = new JsonRpcClient($"http://localhost:{_rpcPort}/jsonrpc", _rpcSecret);
            try
            {
                await WaitForRpcReadyAsync(_rpcClient).ConfigureAwait(false);
            }
            catch
            {
                await ShutdownAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private void PrepareEd2kBootstrapFiles()
    {
        CleanupStaleEd2kSearchDirectories();
        var sourceDirectory = Path.Combine(GetContentRoot(), "Assets", "Ed2k");
        var targetDirectory = Path.Combine(_configDir, "ed2k");
        Directory.CreateDirectory(targetDirectory);

        _ed2kServerListPath = CopyBootstrapFileIfMissing(sourceDirectory, targetDirectory, "server.met");
        _ed2kNodeListPath = CopyBootstrapFileIfMissing(sourceDirectory, targetDirectory, "nodes.dat");
    }

    private static void CleanupStaleEd2kSearchDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "Downio", "ed2k-search");
        if (!Directory.Exists(root)) return;

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string CopyBootstrapFileIfMissing(string sourceDirectory, string targetDirectory, string fileName)
    {
        var sourcePath = Path.Combine(sourceDirectory, fileName);
        var targetPath = Path.Combine(targetDirectory, fileName);
        if (!File.Exists(targetPath) && File.Exists(sourcePath))
        {
            File.Copy(sourcePath, targetPath);
        }
        return File.Exists(targetPath) ? targetPath : string.Empty;
    }

    private static void AddAria2ProxyEnvironmentAliases(ProcessStartInfo startInfo)
    {
        // aria2 only documents the lowercase proxy variable names. .NET accepts
        // both common casings, so add lowercase aliases for aria2 on platforms
        // with case-sensitive environments while preserving lowercase precedence.
        foreach (var lowerName in Aria2ProxyEnvironmentVariables)
        {
            if (Environment.GetEnvironmentVariable(lowerName) != null)
            {
                continue;
            }

            var upperValue = Environment.GetEnvironmentVariable(lowerName.ToUpperInvariant());
            if (upperValue != null)
            {
                startInfo.Environment[lowerName] = upperValue;
            }
        }
    }

    private static async Task<bool> IsRpcReadyAsync(JsonRpcClient client)
    {
        try
        {
            _ = await client.InvokeAsync<Dictionary<string, string>>("getGlobalOption").ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task WaitForRpcReadyAsync(JsonRpcClient client)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (_aria2Process != null && _aria2Process.HasExited)
            {
                var tail = string.Join(" | ", _stderrTail.Reverse().Take(5).Reverse());
                throw new Exception($"aria2 exited early (code: {_aria2Process.ExitCode}). {tail}");
            }

            try
            {
                _ = await client.InvokeAsync<Dictionary<string, string>>("getGlobalOption").ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        throw new Exception("aria2 RPC is not ready.", last);
    }

    private static string NormalizeBtTrackers(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var items = raw
            .Split([',', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var joined = string.Join(',', items);
        const int maxLen = 6144;
        if (joined.Length <= maxLen) return joined;

        var truncated = joined[..maxLen];
        var lastComma = truncated.LastIndexOf(',');
        if (lastComma > 0) truncated = truncated[..lastComma];
        return truncated;
    }

    private static string NormalizeEd2kServers(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return string.Join(',', raw
            .Split([',', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public Task ShutdownAsync()
    {
        if (_rpcClient != null)
        {
            var client = _rpcClient;
            _rpcClient = null;
            _ = TryShutdownRpcAsync(client);
        }

        if (_aria2Process != null && !_aria2Process.HasExited)
        {
            try
            {
                _aria2Process.Kill(entireProcessTree: true);
            }
            catch
            {
                try
                {
                    _aria2Process.Kill();
                }
                catch
                {
                }
            }
        }

        try
        {
            _aria2Process?.Dispose();
        }
        catch
        {
        }
        _aria2Process = null;

        foreach (var pair in _ed2kSearchDirectories.ToArray())
        {
            if (_ed2kSearchDirectories.TryRemove(pair.Key, out var directory))
            {
                TryDeleteDirectory(directory);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task TryShutdownRpcAsync(JsonRpcClient client)
    {
        try
        {
            await client.InvokeAsync<string>("shutdown").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to shutdown aria2 via RPC");
        }
    }

    private string GetBinaryPath()
    {
        var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "aria2c.exe" : "aria2c";
        return Path.Combine(GetContentRoot(), binaryName);
    }

    private static string GetContentRoot()
    {
        var basePath = AppContext.BaseDirectory;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return basePath;

        // The .NET macOS SDK places Content items in Contents/Resources while
        // AppContext.BaseDirectory points to Contents/MacOS in an app bundle.
        var resourcesPath = Path.GetFullPath(Path.Combine(basePath, "..", "Resources"));
        return Directory.Exists(resourcesPath) ? resourcesPath : basePath;
    }

    public async Task<string> AddUriAsync(string url, string filename, string savePath, int split = 16, IDictionary<string, string>? extraOptions = null)
    {
        if (_rpcClient == null) throw new InvalidOperationException("aria2 RPC is not initialized.");

        await RefreshEnvironmentProxyAsync().ConfigureAwait(false);

        var effectiveSplit = Math.Clamp(split, 1, 16);

        var options = new Dictionary<string, string>
        {
            { "dir", savePath },
            { "split", effectiveSplit.ToString() },
            { "max-connection-per-server", effectiveSplit.ToString() },
            { "min-split-size", "1M" }
        };
        
        if (!string.IsNullOrEmpty(filename))
        {
            options["out"] = filename;
        }
        
        if (extraOptions != null)
        {
            foreach (var kv in extraOptions)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                if (kv.Value == null) continue;
                options[kv.Key] = kv.Value;
            }
        }

        // Params: [ [urls], options ]
        var gid = await _rpcClient.InvokeAsync<string>("addUri", new[] { url }, options);
        if (!string.IsNullOrWhiteSpace(gid))
        {
            _splitCache[gid] = effectiveSplit;
        }
        return !string.IsNullOrWhiteSpace(gid)
            ? gid
            : throw new InvalidOperationException("aria2 did not return a download GID.");
    }

    public async Task<string> AddTorrentAsync(string torrentFilePath, string savePath, IDictionary<string, string>? extraOptions = null)
    {
        if (_rpcClient == null) throw new InvalidOperationException("aria2 RPC is not initialized.");
        if (string.IsNullOrWhiteSpace(torrentFilePath) || !File.Exists(torrentFilePath))
            throw new FileNotFoundException("Torrent file was not found.", torrentFilePath);

        try
        {
            await RefreshEnvironmentProxyAsync().ConfigureAwait(false);

            var torrentBytes = await File.ReadAllBytesAsync(torrentFilePath);
            var base64Torrent = Convert.ToBase64String(torrentBytes);

            var options = new Dictionary<string, string>
            {
                { "dir", savePath }
            };

            if (extraOptions != null)
            {
                foreach (var kv in extraOptions)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                    if (kv.Value == null) continue;
                    options[kv.Key] = kv.Value;
                }
            }

            // Params: [ base64Torrent, [uris], options ]
            // Note: [uris] is usually empty for local torrent files
            var gid = await _rpcClient.InvokeAsync<string>("addTorrent", base64Torrent, Array.Empty<string>(), options);
            return !string.IsNullOrWhiteSpace(gid)
                ? gid
                : throw new InvalidOperationException("aria2 did not return a download GID.");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Failed to add torrent: {torrentFilePath}");
            throw;
        }
    }

    public async Task ApplyProxyAsync(string proxyType, string proxyAddress, int proxyPort, string proxyUsername, string proxyPassword)
    {
        if (_rpcClient == null) return;

        var options = new Dictionary<string, string>();

        var address = proxyAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(address) || proxyPort <= 0)
        {
            _usesApplicationProxy = false;
            options = ProxyEnvironment.GetAria2Options();
        }
        else
        {
            _usesApplicationProxy = true;
            var scheme = string.Equals(proxyType, "SOCKS5", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
            var proxy = $"{scheme}://{address}:{proxyPort}";
            var user = proxyUsername?.Trim() ?? string.Empty;
            var password = proxyPassword ?? string.Empty;
            foreach (var prefix in new[] { "http", "https", "ftp", "all" })
            {
                options[$"{prefix}-proxy"] = proxy;
                options[$"{prefix}-proxy-user"] = user;
                options[$"{prefix}-proxy-passwd"] = password;
            }
            options["no-proxy"] = string.Empty;
        }

        try
        {
            await _rpcClient.InvokeAsync<string>("changeGlobalOption", options).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async Task ApplyBtTrackersAsync(string btTrackers)
    {
        if (_rpcClient == null) return;

        var value = NormalizeBtTrackers(btTrackers);
        var options = new Dictionary<string, string>
        {
            ["bt-tracker"] = value
        };

        try
        {
            await _rpcClient.InvokeAsync<string>("changeGlobalOption", options).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async Task<string> StartEd2kSearchAsync(string keyword, string fileType = "", int minSourceCount = 0)
    {
        if (_rpcClient == null) throw new InvalidOperationException("aria2 RPC is not initialized.");
        if (string.IsNullOrWhiteSpace(keyword)) throw new ArgumentException("Search keyword is required.", nameof(keyword));

        await RefreshEnvironmentProxyAsync().ConfigureAwait(false);

        var searchDirectory = Path.Combine(Path.GetTempPath(), "Downio", "ed2k-search", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(searchDirectory);
        var options = new Dictionary<string, string>
        {
            ["dir"] = searchDirectory
        };
        if (!string.IsNullOrWhiteSpace(fileType)) options["fileType"] = fileType.Trim();
        if (minSourceCount > 0) options["minSourceCount"] = minSourceCount.ToString();
        if (!string.IsNullOrWhiteSpace(_ed2kServerListPath)) options["ed2k-server-list"] = _ed2kServerListPath;
        if (!string.IsNullOrWhiteSpace(_ed2kNodeListPath)) options["ed2k-node-list"] = _ed2kNodeListPath;

        try
        {
            var gid = await _rpcClient.InvokeAsync<string>("ed2kSearch", keyword.Trim(), options).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(gid)) throw new InvalidOperationException("aria2 did not return an ED2K search GID.");
            _ed2kSearchDirectories[gid] = searchDirectory;
            return gid;
        }
        catch
        {
            TryDeleteDirectory(searchDirectory);
            throw;
        }
    }

    public async Task<Ed2kSearchResults> GetEd2kSearchResultsAsync(string gid)
    {
        if (_rpcClient == null) throw new InvalidOperationException("aria2 RPC is not initialized.");
        return await _rpcClient.InvokeAsync<Ed2kSearchResults>("getEd2kSearchResults", gid).ConfigureAwait(false)
               ?? new Ed2kSearchResults { Gid = gid };
    }

    public async Task CleanupEd2kSearchAsync(string gid)
    {
        if (_rpcClient != null && !string.IsNullOrWhiteSpace(gid))
        {
            try
            {
                await _rpcClient.InvokeAsync<string>("forceRemove", gid).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Failed to stop ED2K search task {gid}: {ex.Message}");
            }

            try
            {
                await _rpcClient.InvokeAsync<string>("removeDownloadResult", gid).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Failed to remove ED2K search result {gid}: {ex.Message}");
            }
        }

        if (_ed2kSearchDirectories.TryRemove(gid, out var directory))
        {
            TryDeleteDirectory(directory);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to clean ED2K search directory {directory}: {ex.Message}");
        }
    }

    public async Task<List<DownloadTask>> GetGlobalStatusAsync()
    {
        if (_rpcClient == null) return new List<DownloadTask>();

        var tasks = new List<DownloadTask>();

        // We need to fetch Active, Waiting, and Stopped tasks
        // tellActive, tellWaiting, tellStopped
        
        var active = await _rpcClient.InvokeAsync<List<Aria2TaskStatus>>("tellActive");
        var waiting = await _rpcClient.InvokeAsync<List<Aria2TaskStatus>>("tellWaiting", 0, 100);
        var stopped = await _rpcClient.InvokeAsync<List<Aria2TaskStatus>>("tellStopped", 0, 100);

        var gids = Enumerable.Empty<string>();
        if (active != null) gids = gids.Concat(active.Select(s => s.Gid));
        if (waiting != null) gids = gids.Concat(waiting.Select(s => s.Gid));
        if (stopped != null) gids = gids.Concat(stopped.Select(s => s.Gid));
        _ = WarmSplitCacheAsync(gids);

        if (active != null) tasks.AddRange(active.Where(x => !_ed2kSearchDirectories.ContainsKey(x.Gid)).Select(MapToDownloadTask));
        if (waiting != null) tasks.AddRange(waiting.Where(x => !_ed2kSearchDirectories.ContainsKey(x.Gid)).Select(MapToDownloadTask));
        if (stopped != null) tasks.AddRange(stopped.Where(x => !_ed2kSearchDirectories.ContainsKey(x.Gid)).Select(MapToDownloadTask));

        return tasks;
    }

    public async Task PauseAsync(string gid)
    {
        if (_rpcClient == null) return;
        await _rpcClient.InvokeAsync<string>("pause", gid);
    }

    public async Task PauseAllAsync()
    {
        if (_rpcClient == null) return;
        await _rpcClient.InvokeAsync<string>("pauseAll");
    }

    public async Task UnpauseAsync(string gid)
    {
        if (_rpcClient == null) return;
        await RefreshEnvironmentProxyAsync().ConfigureAwait(false);
        await _rpcClient.InvokeAsync<string>("unpause", gid);
    }

    public async Task UnpauseAllAsync()
    {
        if (_rpcClient == null) return;
        await RefreshEnvironmentProxyAsync().ConfigureAwait(false);
        await _rpcClient.InvokeAsync<string>("unpauseAll");
    }

    private async Task RefreshEnvironmentProxyAsync()
    {
        if (_rpcClient == null || _usesApplicationProxy) return;

        try
        {
            await _rpcClient.InvokeAsync<string>("changeGlobalOption", ProxyEnvironment.GetAria2Options()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to refresh proxy environment: {ex.Message}");
        }
    }

    public async Task RemoveAsync(string gid)
    {
        if (_rpcClient == null) return;
        // If active/waiting -> remove
        // If stopped/error -> removeDownloadResult
        
        try 
        {
             await _rpcClient.InvokeAsync<string>("remove", gid);
        }
        catch (Aria2RpcException ex) when (IsGidNotFound(ex))
        {
            return;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Failed to remove task via aria2.remove, fallback to removeDownloadResult: {gid}");
            // Try removeDownloadResult if remove fails (e.g. task is complete/error)
            try
            {
                await _rpcClient.InvokeAsync<string>("removeDownloadResult", gid);
            }
            catch (Aria2RpcException fallbackEx) when (IsGidNotFound(fallbackEx))
            {
                return;
            }
        }
    }

    private static bool IsGidNotFound(Aria2RpcException ex) =>
        ex.RpcMessage.Contains("GID", StringComparison.OrdinalIgnoreCase) &&
        ex.RpcMessage.Contains("not found", StringComparison.OrdinalIgnoreCase);

    private string GetString(string key, string defaultValue)
    {
        if (Application.Current != null && Application.Current.TryFindResource(key, out var resource) && resource is string str)
        {
            return str;
        }
        return defaultValue;
    }

    private DownloadTask MapToDownloadTask(Aria2TaskStatus status)
    {
        long.TryParse(status.TotalLength, out var total);
        long.TryParse(status.CompletedLength, out var completed);
        long.TryParse(status.DownloadSpeed, out var speedVal);

        double progress = total > 0 ? (double)completed / total : 0;
        
        // Map Status
        var taskStatus = status.Status switch
        {
            "active" => "StatusDownloading",
            "waiting" => "StatusWaiting",
            "paused" => "StatusPaused",
            "complete" => "StatusCompleted", // We map this to Stopped in current UI or maybe Completed
            "error" => "StatusError",
            "removed" => "StatusStopped",
            _ => "StatusStopped"
        };
        if (taskStatus == "StatusError")
        {
            var message = string.IsNullOrWhiteSpace(status.ErrorMessage) ? "Unknown error" : status.ErrorMessage;
            AppLog.Warn($"aria2 task error: {status.Gid}, code={status.ErrorCode}, message={message}");
        }

        // Name, Path, Url
        var name = "Unknown";
        var filePath = "";
        var url = "";

        if (status.Files.Any())
        {
            var file = status.Files.First();
            filePath = file.Path;
            if (!string.IsNullOrEmpty(filePath))
            {
                name = Path.GetFileName(filePath);
            }
            
            if (file.Uris.Any())
            {
                url = file.Uris.First().Uri;
            }
        }

        // Time Left
        string timeLeft = "";
        
        if (taskStatus == "StatusPaused" || taskStatus == "StatusWaiting" || taskStatus == "StatusStopped" || taskStatus == "StatusError")
        {
             timeLeft = ""; // No time left for non-active tasks
        }
        else if (speedVal > 0)
        {
            var remaining = total - completed;
            var seconds = remaining / speedVal;
            var ts = TimeSpan.FromSeconds(seconds);
            
            var strMoreThanOneDay = GetString("TimeMoreThanOneDay", "> 1 Day");
            var strDays = GetString("TimeDays", "d");
            var strHours = GetString("TimeHours", "h");
            var strMinutes = GetString("TimeMinutes", "m");
            var strSeconds = GetString("TimeSeconds", "s");

            if (ts.TotalHours >= 24)
            {
                timeLeft = strMoreThanOneDay;
            }
            else
            {
                // Format: mm:ss or hh:mm:ss
                // We need to construct manually to use localized unit strings
                if (ts.TotalHours >= 1)
                {
                    timeLeft = $"{(int)ts.TotalHours}{strHours} {ts.Minutes}{strMinutes} {ts.Seconds}{strSeconds}";
                }
                else
                {
                    timeLeft = $"{ts.Minutes}{strMinutes} {ts.Seconds}{strSeconds}";
                }
            }
        }
        else if (completed == total && total > 0)
        {
            timeLeft = GetString("TimeDone", "Done");
        }
        else
        {
             timeLeft = "--"; // Calculating or stalled
        }

        // Connections
        int.TryParse(status.NumConnections, out var connections);
        var usedUriConnections = status.Files
            .SelectMany(file => file.Uris)
            .Count(uri => string.Equals(uri.Status, "used", StringComparison.OrdinalIgnoreCase));
        if (connections == 0 && usedUriConnections > 0)
        {
            connections = _splitCache.TryGetValue(status.Gid, out var configuredMaximum)
                ? Math.Min(usedUriConnections, configuredMaximum)
                : usedUriConnections;
        }
        if (taskStatus == "StatusDownloading" && connections == 0 && speedVal > 0)
        {
            // Fallback: if downloading but connections report 0, assume at least 1 (e.g. single HTTP stream)
            // or maybe aria2 hasn't updated stats yet.
            // But usually aria2 reports 1 for single connection.
            // If it's HTTP/FTP, numConnections should be valid.
            // If it's BitTorrent, numConnections is peers.
            // Let's trust aria2 but if 0 while downloading, maybe default to 1 for display consistency if speed > 0
            connections = 1;
        }

        var split = _splitCache.TryGetValue(status.Gid, out var configuredSplit)
            ? configuredSplit
            : Math.Max(connections, 1);

        return new DownloadTask
        {
            Id = status.Gid,
            Name = name,
            TotalBytes = total,
            DownloadedBytes = completed,
            Progress = progress,
            Speed = FormatSpeed(speedVal),
            Status = taskStatus,
            TimeLeft = timeLeft,
            Connections = connections,
            Split = split,
            FilePath = filePath,
            FilePaths = status.Files.Select(file => file.Path).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct().ToList(),
            Url = url
        };
    }

    private async Task WarmSplitCacheAsync(IEnumerable<string> gids)
    {
        if (_rpcClient == null) return;

        var unique = gids.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct().ToList();
        if (unique.Count == 0) return;

        foreach (var gid in unique)
        {
            if (_splitCache.ContainsKey(gid)) continue;

            try
            {
                var options = await _rpcClient.InvokeAsync<Dictionary<string, string>>("getOption", gid);
                if (options != null && options.TryGetValue("split", out var splitStr) && int.TryParse(splitStr, out var split) && split > 0)
                {
                    _splitCache[gid] = split;
                }
            }
            catch
            {
            }
        }
    }

    private string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} KB/s";
        return $"{bytesPerSec / 1024.0 / 1024.0:F1} MB/s";
    }

    private string FormatSpeedWithUnit(long bytesPerSec)
    {
        // Just the number and unit, but logic is same as FormatSpeed currently
        // If we want to separate, we can. For now, FormatSpeed returns "10.5 MB/s"
        // The UI requirement is "↓ 10.8 MB/s". We add the arrow in XAML.
        return FormatSpeed(bytesPerSec);
    }

    public void Dispose()
    {
        try
        {
            if (_aria2Process != null && !_aria2Process.HasExited)
            {
                try
                {
                    _aria2Process.Kill(entireProcessTree: true);
                }
                catch
                {
                    _aria2Process.Kill();
                }
            }
        }
        catch
        {
        }

        _aria2Process?.Dispose();
    }
}

// Helper DTOs for JSON Deserialization
public class Aria2TaskStatus
{
    public string Gid { get; set; } = "";
    public string Status { get; set; } = "";
    public string TotalLength { get; set; } = "0";
    public string CompletedLength { get; set; } = "0";
    public string DownloadSpeed { get; set; } = "0";
    public string NumConnections { get; set; } = "0";
    public string ErrorCode { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public List<Aria2File> Files { get; set; } = new();
}

public class Aria2File
{
    public string Path { get; set; } = "";
    public List<Aria2Uri> Uris { get; set; } = new();
}

public class Aria2Uri
{
    public string Uri { get; set; } = "";
    public string Status { get; set; } = "";
}
