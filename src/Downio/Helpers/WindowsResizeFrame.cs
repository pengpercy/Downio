using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Platform;

namespace Downio.Helpers;

/// <summary>
/// On Windows 11 a resizable window with an extended client area keeps the whole sizing frame as
/// non-client area, and Windows paints a sliver of it between our content and the visible border
/// (a gap to the right of the close button). Fixed-size windows have a thinner frame and no gap.
/// This pulls the client edges out to the visible frame bounds while the window isn't maximized.
/// </summary>
internal static unsafe class WindowsResizeFrame
{
    private const uint WM_NCCALCSIZE = 0x0083;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        Win32Properties.AddWndProcHookCallback(window, Hook);
        // The first frame calculation runs before DWM knows the visible bounds, so redo it once shown.
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is { } hWnd)
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        };
    }

    private static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCCALCSIZE || wParam == IntPtr.Zero || IsZoomed(hWnd) || IsIconic(hWnd)) return IntPtr.Zero;
        if (!GetWindowRect(hWnd, out var window) ||
            DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var visible, sizeof(RECT)) != 0)
            return IntPtr.Zero;

        int left = visible.Left - window.Left, right = window.Right - visible.Right, bottom = window.Bottom - visible.Bottom;
        // Before the window is shown the visible bounds aren't known yet; let Avalonia handle it.
        if (left <= 0 || right <= 0 || bottom <= 0) return IntPtr.Zero;

        // rgrc[0] is the first field of NCCALCSIZE_PARAMS: the proposed window rect, which becomes the
        // client rect. The top stays at the window edge, as Avalonia does for an extended title bar.
        var rect = (RECT*)lParam;
        rect->Left += left;
        rect->Right -= right;
        rect->Bottom -= bottom;
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);
}
