using System;
using Avalonia.Input;

namespace Downio.Helpers;

/// <summary>
/// Shortcuts shown in the native menus, using ⌘ on macOS and Ctrl elsewhere.
/// </summary>
public static class MenuGestures
{
    private static readonly KeyModifiers Primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    public static KeyGesture Settings { get; } = new(Key.OemComma, Primary);
    public static KeyGesture Hide { get; } = new(Key.H, Primary);
    public static KeyGesture HideOthers { get; } = new(Key.H, Primary | KeyModifiers.Alt);
    public static KeyGesture Quit { get; } = new(Key.Q, Primary);

    public static KeyGesture NewTask { get; } = new(Key.N, Primary);
    public static KeyGesture OpenDownloadFolder { get; } = new(Key.O, Primary | KeyModifiers.Shift);
    public static KeyGesture CloseWindow { get; } = new(Key.W, Primary);

    public static KeyGesture ResumeAll { get; } = new(Key.R, Primary | KeyModifiers.Alt);
    public static KeyGesture PauseAll { get; } = new(Key.P, Primary | KeyModifiers.Alt);

    public static KeyGesture ShowDownloading { get; } = new(Key.D1, Primary);
    public static KeyGesture ShowWaiting { get; } = new(Key.D2, Primary);
    public static KeyGesture ShowStopped { get; } = new(Key.D3, Primary);
    public static KeyGesture ShowEd2kSearch { get; } = new(Key.D4, Primary);
    public static KeyGesture ToggleSidebar { get; } = new(Key.S, Primary | KeyModifiers.Control);

    public static KeyGesture Minimize { get; } = new(Key.M, Primary);
    public static KeyGesture MainWindow { get; } = new(Key.D0, Primary);
}
