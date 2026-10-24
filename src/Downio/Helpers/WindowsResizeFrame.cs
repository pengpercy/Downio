using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Platform;

namespace Downio.Helpers;

/// <summary>
/// With an extended client area Avalonia 12 keeps the whole sizing frame (8px at 100%) as non-client
/// area on the left, right and bottom of a resizable window, but only part of it is the visible frame:
/// the rest shows as a strip between our content and the window border.
/// Fixed-size windows have a thin frame and no strip. This pulls the client edges out to the visible
/// border while the window isn't maximized; resizing still works through Avalonia's hit testing.
/// </summary>
internal static class WindowsResizeFrame
{
    private const uint WM_NCCALCSIZE = 0x0083;
    private const int GWL_STYLE = -16;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const int SM_CXBORDER = 5;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    public static void Attach(Window window)
    {
        // The per-DPI metrics APIs below arrived in Windows 10 1607
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)) return;
        Win32Properties.AddWndProcHookCallback(window, Hook);
        // Recalculate the frame once shown so the first layout already uses the wider client area.
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is { } hWnd)
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        };
    }

    private static unsafe IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCCALCSIZE || wParam == IntPtr.Zero || IsZoomed(hWnd) || IsIconic(hWnd)) return IntPtr.Zero;

        var style = (uint)GetWindowLongW(hWnd, GWL_STYLE);
        if ((style & WS_THICKFRAME) == 0 || (style & WS_CAPTION) != WS_CAPTION) return IntPtr.Zero;

        // Line the client edges up with the visible frame DWM reports (10px in at 200% while Avalonia
        // takes 13px); fall back to the sizing frame minus a 1px border before DWM knows the bounds.
        int left, right, bottom;
        if (GetWindowRect(hWnd, out var window) &&
            DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var visible, sizeof(RECT)) == 0 &&
            visible.Left > window.Left && visible.Right < window.Right && visible.Bottom < window.Bottom)
        {
            (left, right, bottom) = (visible.Left - window.Left, window.Right - visible.Right, window.Bottom - visible.Bottom);
        }
        else
        {
            var dpi = GetDpiForWindow(hWnd);
            var frame = new RECT();
            if (dpi == 0 || !AdjustWindowRectExForDpi(ref frame, style, false, 0, dpi)) return IntPtr.Zero;
            var border = Math.Max(1, GetSystemMetricsForDpi(SM_CXBORDER, dpi));
            (left, right, bottom) = (-frame.Left - border, frame.Right - border, frame.Bottom - border);
        }
        if (left < 0 || right < 0 || bottom < 0) return IntPtr.Zero;

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

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustWindowRectExForDpi(ref RECT rect, uint style,
        [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle, uint dpi);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
