using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Downio.Models;

public partial class DownloadTask : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeInfo))]
    private long _totalBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeInfo))]
    private long _downloadedBytes;

    [ObservableProperty]
    private double _progress; // 0.0 to 1.0

    [ObservableProperty]
    private string _speed = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private string _status = "StatusDownloading"; // StatusDownloading, StatusCompleted, StatusPaused, StatusError

    public bool IsActive => Status == "StatusDownloading" || Status == "StatusWaiting";

    [ObservableProperty]
    private string _timeLeft = string.Empty;

    [ObservableProperty]
    private int _connections;

    [ObservableProperty]
    private int _split = 1;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    public string SizeInfo => $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes)}";

    private string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        double number = bytes;
        while (Math.Abs(number) >= 1024 && counter < suffixes.Length - 1)
        {
            counter++;
            number /= 1024;
        }
        return $"{number:0.##} {suffixes[counter]}";
    }
}
