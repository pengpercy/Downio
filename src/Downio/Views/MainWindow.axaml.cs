using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
        // AppKit puts the traffic lights back in its default spot on these
        Resized += (_, _) => UpdateMacTitleBarInsets();
        Activated += (_, _) => UpdateMacTitleBarInsets();
        // The appearance change relays out the title bar after this event, so wait for it
        ActualThemeVariantChanged += (_, _) =>
            Dispatcher.UIThread.Post(UpdateMacTitleBarInsets, DispatcherPriority.Background);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                UpdateMacTitleBarInsets();
            }
            else if (e.Property == ActualTransparencyLevelProperty)
            {
                // Only Windows 11 grants Mica; elsewhere the hint falls back to acrylic blur.
                Classes.Set("mica", ActualTransparencyLevel == WindowTransparencyLevel.Mica);
            }
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) vm.PropertyChanged += ViewModel_PropertyChanged;
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Land in the toolbar search box whenever the ED2K page is opened.
        if (e.PropertyName == nameof(MainWindowViewModel.IsEd2kSearch) &&
            sender is MainWindowViewModel { IsEd2kSearch: true })
        {
            Dispatcher.UIThread.Post(() => Ed2kToolbar.FocusSearchBox(), DispatcherPriority.Input);
        }
    }

    private void Sidebar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.IsMacOS)
        {
            return;
        }

        // Double-click maximize/zoom is already handled by
        // chrome:WindowDecorationProperties.ElementRole="TitleBar".
        // Manually toggling WindowState here races with that native handler
        // and briefly maximizes then restores. Only assist single-click drag.
        if (e.ClickCount == 1 &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            !IsInteractiveTitleBarElement(e.Source))
        {
            BeginMoveDrag(e);
        }
    }

    private static bool IsInteractiveTitleBarElement(object? source)
    {
        return source is Control control &&
               control.GetSelfAndVisualAncestors().Any(v => v is Button or TextBox or ComboBox);
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
            // Full screen moves the title bar into its own window; leave AppKit's layout alone there
            if (WindowState != WindowState.FullScreen)
            {
                MacTitleBarInsets.TryCenterTrafficLights(handle, titleBarHeight);
            }
            if (MacTitleBarInsets.TryGetTrafficLightsRight(handle, out var right))
            {
                trafficLightsRight = right;
            }
        }

        vm.UpdateMacTitleBarInsets(trafficLightsRight, titleBarHeight);
    }

}
