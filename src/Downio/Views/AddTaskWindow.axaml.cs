using System;
using System.Linq;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Downio.ViewModels;

namespace Downio.Views;

public partial class AddTaskWindow : DialogWindow
{
    private readonly MainWindowViewModel? _viewModel;

    public AddTaskWindow()
    {
        InitializeComponent();
    }

    public AddTaskWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        // Execute the view model command
        if (_viewModel != null && _viewModel.StartDownloadCommand.CanExecute(null))
        {
            _viewModel.StartDownloadCommand.Execute(null);
            Close();
        }
    }

    private void OnTorrentDragOver(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnTorrentDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (_viewModel != null && !string.IsNullOrWhiteSpace(path) && path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.NewTaskTorrentFilePath = path;
        }
    }

    private async void OnTorrentPick(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel == null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Torrent File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Torrent")
                {
                    Patterns = ["*.torrent"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _viewModel.NewTaskTorrentFilePath = path;
        }
    }
}
