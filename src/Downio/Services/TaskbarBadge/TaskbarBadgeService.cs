using System;
using Avalonia.Controls;

#if WINDOWS
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
#elif MACOS
using System.Runtime.InteropServices;
using Avalonia.Threading;
#endif

namespace Downio.Services.TaskbarBadge;

/// <summary>
/// Displays a native Windows taskbar overlay or macOS Dock badge. Other platforms intentionally do nothing.
/// </summary>
public sealed class TaskbarBadgeService : ITaskbarBadgeService
{
#if WINDOWS
    private ITaskbarList3? _taskbarList;
    private IntPtr _windowHandle;
#endif

#if WINDOWS || MACOS
    private long? _lastSpeed;
#endif

    public void Attach(Window window)
    {
#if WINDOWS
        ArgumentNullException.ThrowIfNull(window);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _windowHandle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_windowHandle == IntPtr.Zero)
        {
            AppLog.Warn("Unable to acquire the main window handle for the taskbar download-speed badge.");
            return;
        }

        try
        {
            _taskbarList ??= (ITaskbarList3)new CTaskbarList();
            _taskbarList.HrInit();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Taskbar download-speed badge initialization failed");
            _taskbarList = null;
        }
#else
        _ = window;
#endif
    }

    public void Update(long totalDownloadSpeedBytesPerSecond)
    {
#if WINDOWS
        if (_taskbarList is null || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        var normalizedSpeed = Math.Max(0, totalDownloadSpeedBytesPerSecond);
        if (_lastSpeed == normalizedSpeed)
        {
            return;
        }

        _lastSpeed = normalizedSpeed;
        if (normalizedSpeed == 0)
        {
            Clear();
            return;
        }

        IntPtr icon = IntPtr.Zero;
        try
        {
            icon = CreateBadgeIcon(TaskbarBadgeTextFormatter.Format(normalizedSpeed));
            var description = $"Downloading at {TaskbarBadgeTextFormatter.FormatDescription(normalizedSpeed)}";
            _taskbarList.SetOverlayIcon(_windowHandle, icon, description);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Taskbar download-speed badge update failed");
        }
        finally
        {
            if (icon != IntPtr.Zero)
            {
                DestroyIcon(icon);
            }
        }
#elif MACOS
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var normalizedSpeed = Math.Max(0, totalDownloadSpeedBytesPerSecond);
        if (_lastSpeed == normalizedSpeed)
        {
            return;
        }

        _lastSpeed = normalizedSpeed;
        SetMacDockBadge(normalizedSpeed == 0 ? null : TaskbarBadgeTextFormatter.Format(normalizedSpeed));
#else
        _ = totalDownloadSpeedBytesPerSecond;
#endif
    }

    public void Clear()
    {
#if WINDOWS || MACOS
        _lastSpeed = 0;
#endif

#if WINDOWS
        if (_taskbarList is null || _windowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _taskbarList.SetOverlayIcon(_windowHandle, IntPtr.Zero, null);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Taskbar download-speed badge clear failed");
        }
#elif MACOS
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        SetMacDockBadge(null);
#endif
    }

    public void Dispose()
    {
        Clear();

#if WINDOWS
        if (_taskbarList is not null)
        {
            Marshal.ReleaseComObject(_taskbarList);
            _taskbarList = null;
        }

        _windowHandle = IntPtr.Zero;
#endif

#if WINDOWS || MACOS
        _lastSpeed = null;
#endif
    }

#if MACOS && !WINDOWS
    private static void SetMacDockBadge(string? text)
    {
        void SetBadge()
        {
            try
            {
                var applicationClass = objc_getClass("NSApplication");
                var application = IntPtr_objc_msgSend(applicationClass, sel_registerName("sharedApplication"));
                var dockTile = IntPtr_objc_msgSend(application, sel_registerName("dockTile"));
                if (dockTile == IntPtr.Zero)
                {
                    return;
                }

                IntPtr label = IntPtr.Zero;
                try
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        label = CFStringCreateWithCString(IntPtr.Zero, text, kCFStringEncodingUTF8);
                    }

                    Void_objc_msgSend_IntPtr(dockTile, sel_registerName("setBadgeLabel:"), label);
                }
                finally
                {
                    if (label != IntPtr.Zero)
                    {
                        CFRelease(label);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error(ex, "macOS Dock download-speed badge update failed");
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            SetBadge();
            return;
        }

        Dispatcher.UIThread.Post(SetBadge);
    }

    private const uint kCFStringEncodingUTF8 = 0x08000100;
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFStringCreateWithCString", CharSet = CharSet.Ansi)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string value, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr cf);
#endif

#if WINDOWS
    private static IntPtr CreateBadgeIcon(string text)
    {
        const int size = 64;
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        using var background = new SolidBrush(Color.FromArgb(230, 220, 38, 38));
        using var border = new Pen(Color.White, 3);
        graphics.FillEllipse(background, 2, 2, size - 4, size - 4);
        graphics.DrawEllipse(border, 3, 3, size - 6, size - 6);

        var fontSize = text.Length switch
        {
            <= 3 => 25f,
            <= 5 => 18f,
            _ => 14f
        };
        using var font = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        var bounds = new RectangleF(3, 3, size - 6, size - 6);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, textBrush, bounds, format);

        return bitmap.GetHicon();
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class CTaskbarList;

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, TaskbarProgressState flags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint buttonCount, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint buttonCount, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
    }

    private enum TaskbarProgressState
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
#endif
}
