namespace Downio.Models;

public class AppSettings
{
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "System";
    public bool AutoStart { get; set; } = false;
    public string DefaultSavePath { get; set; } = string.Empty;
    public int DefaultDownloadSplit { get; set; } = 16;
    public string AccentMode { get; set; } = "System";
    public string CustomAccentColor { get; set; } = string.Empty;
    public bool ExitOnClose { get; set; } = false;
    public string ProxyType { get; set; } = "HTTP";
    public string ProxyAddress { get; set; } = string.Empty;
    public int ProxyPort { get; set; } = 8080;
    public string ProxyUsername { get; set; } = string.Empty;
    public string ProxyPassword { get; set; } = string.Empty;
    public bool AutoInstallUpdates { get; set; } = false;
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
    public string SkipVersion { get; set; } = string.Empty;

    // Advanced Settings
    public string BtTrackers { get; set; } = string.Empty;
    public int RpcPort { get; set; } = 16800;
    public string RpcSecret { get; set; } = "DownioSecret";
    public bool EnableUpnp { get; set; } = false;
    public int BtListenPort { get; set; } = 6881;
    public int DhtListenPort { get; set; } = 6881;
    public int Ed2kListenPort { get; set; } = 4662;
    public int Ed2kUdpListenPort { get; set; } = 4672;
    public string Ed2kServer { get; set; } = string.Empty;
    public string Ed2kServerMetUrl { get; set; } = "https://upd.emule-security.org/server.met";
    public string Ed2kNodesDatUrl { get; set; } = "https://upd.emule-security.org/nodes.dat";
    public bool Ed2kBootstrapAutoSync { get; set; } = true;
    public int Ed2kBootstrapSyncIntervalHours { get; set; } = 24;
    public int Ed2kUploadSlots { get; set; } = 3;
    public int Ed2kMinSourceCount { get; set; } = 0;
    public int Ed2kSearchTimeout { get; set; } = 20;
    public string GlobalUserAgent { get; set; } = string.Empty;
    public bool DefaultClientMagnet { get; set; } = true;
    public bool DefaultClientThunder { get; set; } = true;
    public System.Collections.Generic.List<string> TrackerSources { get; set; } =
    [
        "https://cdn.jsdelivr.net/gh/ngosang/trackerslist/trackers_best_ip.txt",
        "https://cdn.jsdelivr.net/gh/ngosang/trackerslist/trackers_best.txt"
    ];
    public System.Collections.Generic.List<string> CustomTrackerSources { get; set; } = new();
    public bool AutoSyncTracker { get; set; } = true;
    public long LastSyncTrackerTime { get; set; } = 0;
}
