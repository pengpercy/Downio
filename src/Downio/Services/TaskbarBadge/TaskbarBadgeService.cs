using System;
using Avalonia.Controls;
using Avalonia.Threading;

#if WINDOWS
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
#elif MACOS
using System.Runtime.InteropServices;
#endif

namespace Downio.Services.TaskbarBadge;

/// <summary>
/// Shows the aggregate download speed as a Windows taskbar overlay or a macOS Dock badge
/// (the red pill at the top right of the Dock icon). Other platforms intentionally do nothing.
/// </summary>
/// <remarks>
/// Samples arrive once a second and are eased before display, so the badge drifts between
/// values instead of jumping on every aria2 fluctuation, and brief stalls don't make it blink.
/// The native badge is only touched when its rounded text actually changes.
/// </remarks>
public sealed unsafe class TaskbarBadgeService : ITaskbarBadgeService
{
    // Weight of the newest sample; about two seconds to settle after a real change.
    private const double Smoothing = 0.45;

    // Consecutive zero samples before the badge goes away.
    private const int ZeroSamplesBeforeHide = 2;

    private double _smoothedSpeed;
    private int _zeroSamples;
    private string? _shownText;

#if WINDOWS
    private const int SmallIconMetric = 49; // SM_CXSMICON
    private static readonly uint TaskbarButtonCreatedMessage = RegisterWindowMessageW("TaskbarButtonCreated");
    private IntPtr _taskbarList;
    private IntPtr _windowHandle;
    private IntPtr _overlayIcon;
#endif

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
#if WINDOWS
        if (_windowHandle != IntPtr.Zero) return;

        _windowHandle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_windowHandle == IntPtr.Zero)
        {
            AppLog.Warn("Unable to acquire the main window handle for the taskbar download-speed badge.");
            return;
        }

        // Explorer (re)creates the taskbar button when the window is first shown, shown again after
        // hiding to the tray, or after Explorer restarts; an overlay set before that is lost.
        Win32Properties.AddWndProcHookCallback(window, (IntPtr _, uint msg, IntPtr _, IntPtr _, ref bool _) =>
        {
            if (msg == TaskbarButtonCreatedMessage) Dispatcher.UIThread.Post(ReapplyOverlay);
            return IntPtr.Zero;
        });
#endif
    }

    public void Update(long totalDownloadSpeedBytesPerSecond)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Update(totalDownloadSpeedBytesPerSecond));
            return;
        }

        var speed = Math.Max(0, totalDownloadSpeedBytesPerSecond);
        if (speed == 0)
        {
            if (++_zeroSamples >= ZeroSamplesBeforeHide) Clear();
            return;
        }

        _zeroSamples = 0;
        _smoothedSpeed = _smoothedSpeed <= 0 ? speed : _smoothedSpeed + Smoothing * (speed - _smoothedSpeed);
        Show((long)Math.Round(_smoothedSpeed));
    }

    public void Clear()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Clear);
            return;
        }

        _smoothedSpeed = 0;
        _zeroSamples = 0;
        if (_shownText == null) return;
        _shownText = null;

#if WINDOWS
        SetOverlay(null, null);
#elif MACOS
        SetDockBadge(null);
#endif
    }

    public void Dispose()
    {
        Clear();
#if WINDOWS
        ReleaseOverlayIcon();
        ReleaseTaskbarList();
        _windowHandle = IntPtr.Zero;
#endif
    }

    private void Show(long speed)
    {
#if WINDOWS
        var text = TaskbarBadgeTextFormatter.FormatOverlay(speed);
        if (text == _shownText) return;
        _shownText = text;
        SetOverlay(text, TaskbarBadgeTextFormatter.FormatDescription(speed));
#elif MACOS
        var text = TaskbarBadgeTextFormatter.Format(speed);
        if (text == _shownText) return;
        _shownText = text;
        SetDockBadge(text);
#else
        _ = speed;
#endif
    }

#if MACOS && !WINDOWS
    private static void SetDockBadge(string? text)
    {
        try
        {
            var application = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            var dockTile = Send(application, sel_registerName("dockTile"));
            if (dockTile == IntPtr.Zero) return;

            var label = string.IsNullOrEmpty(text)
                ? IntPtr.Zero
                : SendUtf8(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), text);
            SendVoid(dockTile, sel_registerName("setBadgeLabel:"), label);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "macOS Dock download-speed badge update failed");
        }
    }

    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg1);
#endif

#if WINDOWS
    private void ReapplyOverlay()
    {
        // The new button has no overlay: drop the cached state and repaint the current speed.
        ReleaseTaskbarList();
        var text = _shownText;
        _shownText = null;
        if (text != null && _smoothedSpeed > 0) Show((long)Math.Round(_smoothedSpeed));
    }

    private void SetOverlay(string? text, string? description)
    {
        if (_windowHandle == IntPtr.Zero) return;

        IntPtr icon = IntPtr.Zero;
        try
        {
            if (_taskbarList == IntPtr.Zero && !TryCreateTaskbarList()) return;

            if (text != null) icon = CreateBadgeIcon(text, OverlayIconSize());
            fixed (char* descriptionPtr = description)
            {
                // ITaskbarList3::SetOverlayIcon
                var setOverlayIcon = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, char*, int>)VTable(_taskbarList, 18);
                var hr = setOverlayIcon(_taskbarList, _windowHandle, icon, descriptionPtr);
                if (hr < 0)
                {
                    // Usually "no taskbar button yet"; TaskbarButtonCreated will retry.
                    _shownText = null;
                    return;
                }
            }

            ReleaseOverlayIcon();
            _overlayIcon = icon;
            icon = IntPtr.Zero;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Taskbar download-speed badge update failed");
        }
        finally
        {
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        }
    }

    private bool TryCreateTaskbarList()
    {
        var classId = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
        var interfaceId = new Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84"); // ITaskbarList3
        if (CoCreateInstance(&classId, IntPtr.Zero, 0x1 /* CLSCTX_INPROC_SERVER */, &interfaceId, out var taskbarList) < 0)
        {
            return false;
        }

        // ITaskbarList::HrInit
        var hrInit = (delegate* unmanaged[Stdcall]<IntPtr, int>)VTable(taskbarList, 3);
        if (hrInit(taskbarList) < 0)
        {
            Release(taskbarList);
            return false;
        }

        _taskbarList = taskbarList;
        return true;
    }

    // Overlays are drawn at the small-icon size for the window's DPI (16 px at 100%, 24 px at 150%),
    // so the text is rendered crisp at its final size rather than shrunk from a large bitmap.
    private int OverlayIconSize()
    {
        try
        {
            var dpi = GetDpiForWindow(_windowHandle);
            var size = dpi > 0 ? GetSystemMetricsForDpi(SmallIconMetric, dpi) : 16;
            return Math.Clamp(size, 16, 64);
        }
        catch (EntryPointNotFoundException)
        {
            return 16;
        }
    }

    private static IntPtr CreateBadgeIcon(string text, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        // Grayscale antialiasing: ClearType fringes look dirty on a transparent overlay.
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        graphics.Clear(Color.Transparent);

        // A rounded pill in the system badge red with a thin light rim, so it reads on any icon.
        var bounds = new RectangleF(0.5f, 0.5f, size - 1f, size - 1f);
        using var pill = RoundedRectangle(bounds, size * 0.3f);
        using var fill = new SolidBrush(Color.FromArgb(255, 232, 59, 48));
        using var rim = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1f, size / 16f));
        graphics.FillPath(fill, pill);
        graphics.DrawPath(rim, pill);

        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
        };
        using var family = TryCreateFamily("Segoe UI") ?? new FontFamily(GenericFontFamilies.SansSerif);

        // Largest bold size that fits inside the rim.
        var available = size - Math.Max(3f, size * 0.18f);
        var fontSize = size * 0.62f;
        Font font = new(family, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        while (fontSize > 5f && graphics.MeasureString(text, font, PointF.Empty, format).Width > available)
        {
            font.Dispose();
            fontSize -= 0.5f;
            font = new Font(family, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        using (font)
        using (var textBrush = new SolidBrush(Color.White))
        {
            graphics.DrawString(text, font, textBrush, new RectangleF(0, 0.5f, size, size), format);
        }

        return bitmap.GetHicon();
    }

    private static GraphicsPath RoundedRectangle(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static FontFamily? TryCreateFamily(string name)
    {
        try
        {
            return new FontFamily(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void ReleaseOverlayIcon()
    {
        if (_overlayIcon == IntPtr.Zero) return;
        DestroyIcon(_overlayIcon);
        _overlayIcon = IntPtr.Zero;
    }

    private void ReleaseTaskbarList()
    {
        if (_taskbarList == IntPtr.Zero) return;
        Release(_taskbarList);
        _taskbarList = IntPtr.Zero;
    }

    // Plain vtable calls: NativeAOT can't activate [ComImport] coclasses, and function pointers
    // avoid the delegate marshalling stubs GetDelegateForFunctionPointer would need.
    private static void* VTable(IntPtr instance, int slot) => (*(void***)instance)[slot];

    private static void Release(IntPtr instance) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(instance, 2))(instance);

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(Guid* classId, IntPtr outer, uint context, Guid* interfaceId, out IntPtr instance);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
#endif
}
