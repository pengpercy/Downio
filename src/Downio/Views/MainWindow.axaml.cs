using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Downio.Helpers;
using Downio.ViewModels;

namespace Downio.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => UpdateMacTitleBarInsets();
        ScalingChanged += (_, _) => UpdateMacTitleBarInsets();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateMacTitleBarInsets();
            }
        };
    }

    private void Sidebar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(e);
        }
    }

    private void UpdateMacTitleBarInsets()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.IsMacLikeLayout) return;

        var titleBarHeight = ExtendClientAreaTitleBarHeightHint > 0 ? ExtendClientAreaTitleBarHeightHint : 44d;

        var trafficLightsRight = 60d;
        if (vm.IsMacOS)
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (MacTitleBarInsets.TryGetTrafficLightsRight(handle, out var right))
            {
                trafficLightsRight = right;
            }
        }

        vm.UpdateMacTitleBarInsets(trafficLightsRight, titleBarHeight);
    }

}
