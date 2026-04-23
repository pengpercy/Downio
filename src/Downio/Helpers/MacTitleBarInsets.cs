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

    [DllImport(ObjcLib)]
    private static extern IntPtr sel_registerName(string name);

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
