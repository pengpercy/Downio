using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Downio.Models;
using Downio.Services;

namespace Downio.Views;

public partial class TaskDetailsWindow : DialogWindow
{
    private readonly Func<DownloadTask, Task>? _openFolder;

    public TaskDetailsWindow()
    {
        InitializeComponent();
    }

    public TaskDetailsWindow(DownloadTask task, Func<DownloadTask, Task>? openFolder = null) : this()
    {
        DataContext = task;
        _openFolder = openFolder;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text } button || string.IsNullOrEmpty(text) || Clipboard == null) return;

        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(text));
            await Clipboard.SetDataAsync(data);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Copy from task details failed");
            return;
        }

        // Briefly swap the copy icon for a checkmark as confirmation.
        if (button.Content is not PathIcon icon || !this.TryFindResource("IconCheck", out var check)) return;
        var original = icon.Data;
        icon.Data = check as Avalonia.Media.Geometry;
        DispatcherTimer.RunOnce(() => icon.Data = original, TimeSpan.FromSeconds(1.2));
    }

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_openFolder != null && DataContext is DownloadTask task)
        {
            _ = _openFolder(task);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
