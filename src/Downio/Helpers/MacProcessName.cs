using System;
using System.Runtime.InteropServices;

namespace Downio.Helpers;

internal static class MacProcessName
{
    public static void TrySet(string processName)
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        try
        {
            var nsProcessInfoClass = objc_getClass("NSProcessInfo");
            if (nsProcessInfoClass == IntPtr.Zero)
            {
                return;
            }

            var processInfoSelector = sel_registerName("processInfo");
            var setProcessNameSelector = sel_registerName("setProcessName:");
            if (processInfoSelector == IntPtr.Zero || setProcessNameSelector == IntPtr.Zero)
            {
                return;
            }

            var processInfo = IntPtr_objc_msgSend(nsProcessInfoClass, processInfoSelector);
            if (processInfo == IntPtr.Zero)
            {
                return;
            }

            var nameHandle = CFStringCreateWithCString(IntPtr.Zero, processName, kCFStringEncodingUTF8);
            if (nameHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                Void_objc_msgSend_IntPtr(processInfo, setProcessNameSelector, nameHandle);
            }
            finally
            {
                CFRelease(nameHandle);
            }
        }
        catch
        {
            // Ignore failures and allow Avalonia/platform defaults to continue.
        }
    }

    private const uint kCFStringEncodingUTF8 = 0x08000100;

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass", CharSet = CharSet.Ansi)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName", CharSet = CharSet.Ansi)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFStringCreateWithCString", CharSet = CharSet.Ansi)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr cf);
}
