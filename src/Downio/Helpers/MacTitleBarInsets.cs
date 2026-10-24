using System;
using System.Runtime.InteropServices;

namespace Downio.Helpers;

public static class MacTitleBarInsets
{
    private const string ObjcLib = "/usr/lib/libobjc.A.dylib";

    private enum NSWindowButton : int
    {
        Close = 0,
        Miniaturize = 1,
        Zoom = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [DllImport(ObjcLib)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_CGPoint(IntPtr receiver, IntPtr selector, CGPoint arg1);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_CGRect(IntPtr receiver, IntPtr selector, CGRect arg1);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr_nint(IntPtr receiver, IntPtr selector, nint arg1);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern CGRect objc_msgSend_CGRect(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend_stret")]
    private static extern void objc_msgSend_stret(out CGRect ret, IntPtr receiver, IntPtr selector);

    public static bool TryGetTrafficLightsRight(IntPtr nsWindowHandle, out double right)
    {
        right = 0;
        if (nsWindowHandle == IntPtr.Zero) return false;

        try
        {
            var selStandardWindowButton = sel_registerName("standardWindowButton:");
            var selFrame = sel_registerName("frame");

            var zoomButton = objc_msgSend_IntPtr_nint(nsWindowHandle, selStandardWindowButton, (nint)NSWindowButton.Zoom);
            if (zoomButton == IntPtr.Zero) return false;

            var rect = GetFrame(zoomButton, selFrame);
            if (rect.Width <= 0) return false;

            right = rect.X + rect.Width;
            return right > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// AppKit lays the traffic lights out for its own 28pt title bar, which leaves them
    /// cramped in the corner of a taller custom one. Centre them vertically in
    /// <paramref name="titleBarHeight"/> and give them the same gap on the left.
    /// AppKit can reset the layout (resize, activation, full screen), so call this again then.
    /// </summary>
    public static bool TryCenterTrafficLights(IntPtr nsWindowHandle, double titleBarHeight)
    {
        if (nsWindowHandle == IntPtr.Zero) return false;

        try
        {
            var selStandardWindowButton = sel_registerName("standardWindowButton:");
            var selFrame = sel_registerName("frame");
            var selSuperview = sel_registerName("superview");
            var selSetFrameOrigin = sel_registerName("setFrameOrigin:");
            var selSetFrame = sel_registerName("setFrame:");

            var buttons = new[]
            {
                objc_msgSend_IntPtr_nint(nsWindowHandle, selStandardWindowButton, (nint)NSWindowButton.Close),
                objc_msgSend_IntPtr_nint(nsWindowHandle, selStandardWindowButton, (nint)NSWindowButton.Miniaturize),
                objc_msgSend_IntPtr_nint(nsWindowHandle, selStandardWindowButton, (nint)NSWindowButton.Zoom)
            };
            if (Array.IndexOf(buttons, IntPtr.Zero) >= 0) return false;

            // close button -> NSTitlebarView -> NSTitlebarContainerView -> NSThemeFrame
            var titlebarView = objc_msgSend_IntPtr(buttons[0], selSuperview);
            var container = titlebarView == IntPtr.Zero ? IntPtr.Zero : objc_msgSend_IntPtr(titlebarView, selSuperview);
            var themeFrame = container == IntPtr.Zero ? IntPtr.Zero : objc_msgSend_IntPtr(container, selSuperview);
            if (themeFrame == IntPtr.Zero) return false;

            var titlebarFrame = GetFrame(titlebarView, selFrame);
            var themeFrameRect = GetFrame(themeFrame, selFrame);
            var closeFrame = GetFrame(buttons[0], selFrame);
            var spacing = GetFrame(buttons[1], selFrame).X - closeFrame.X;
            if (titlebarFrame.Height <= 0 || closeFrame.Height <= 0 || spacing <= 0) return false;

            // Grow the container downwards so its top (which carries the window's rim highlight)
            // stays at the window edge, then sit the title bar view at its bottom. The title bar
            // view keeps AppKit's height, because Avalonia reads it for title bar hit testing.
            var drop = Math.Max(0, (titleBarHeight - titlebarFrame.Height) / 2);
            var containerHeight = titlebarFrame.Height + drop;
            objc_msgSend_void_CGRect(container, selSetFrame, new CGRect
            {
                X = 0,
                Y = themeFrameRect.Height - containerHeight,
                Width = themeFrameRect.Width,
                Height = containerHeight
            });
            objc_msgSend_void_CGRect(titlebarView, selSetFrame, new CGRect
            {
                Width = themeFrameRect.Width,
                Height = titlebarFrame.Height
            });

            var left = Math.Max(0, (titleBarHeight - closeFrame.Height) / 2);
            for (var i = 0; i < buttons.Length; i++)
            {
                var frame = GetFrame(buttons[i], selFrame);
                objc_msgSend_void_CGPoint(buttons[i], selSetFrameOrigin, new CGPoint { X = left + i * spacing, Y = frame.Y });
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static CGRect GetFrame(IntPtr view, IntPtr selFrame)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            objc_msgSend_stret(out var rect, view, selFrame);
            return rect;
        }

        return objc_msgSend_CGRect(view, selFrame);
    }
}
