namespace Downio.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Downio.Models;
using Downio.ViewModels;

public partial class SettingsWindow : DialogWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private async void OnCopyTrackerSource(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not Button { DataContext: TrackerSourceOption option }) return;
        await vm.CopyText(option.Url);
    }

    private void OnRemoveTrackerSource(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not Button { DataContext: TrackerSourceOption option }) return;
        vm.RemoveTrackerSource(option);
    }
}
