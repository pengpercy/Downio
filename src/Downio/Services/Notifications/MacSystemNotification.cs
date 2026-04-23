using System;
using System.Runtime.InteropServices;

namespace Downio.Services.Notifications;

public static class MacSystemNotification
{
    public static bool TryShow(string title, string message)
    {
#if !MACOS
        return false;
#else
        try
        {
            var nsUserNotificationCenterClass = ObjC.GetClass("NSUserNotificationCenter");
            var defaultCenterSel = ObjC.GetSelector("defaultUserNotificationCenter");
            var center = ObjC.SendIntPtr(nsUserNotificationCenterClass, defaultCenterSel);
            if (center == IntPtr.Zero) return false;

            var nsUserNotificationClass = ObjC.GetClass("NSUserNotification");
            var allocSel = ObjC.GetSelector("alloc");
            var initSel = ObjC.GetSelector("init");
            var notification = ObjC.SendIntPtr(ObjC.SendIntPtr(nsUserNotificationClass, allocSel), initSel);
            if (notification == IntPtr.Zero) return false;

            var setTitleSel = ObjC.GetSelector("setTitle:");
            var setInformativeTextSel = ObjC.GetSelector("setInformativeText:");
            ObjC.SendVoid_IntPtr(notification, setTitleSel, ObjC.CreateNSString(title));
            ObjC.SendVoid_IntPtr(notification, setInformativeTextSel, ObjC.CreateNSString(message));

            var deliverSel = ObjC.GetSelector("deliverNotification:");
            ObjC.SendVoid_IntPtr(center, deliverSel, notification);

            return true;
        }
        catch
        {
            return false;
        }
#endif
    }

#if MACOS
    private static class ObjC
    {
        private const string LibObjC = "/usr/lib/libobjc.A.dylib";

        [DllImport(LibObjC)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(LibObjC)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        public static IntPtr GetClass(string name) => objc_getClass(name);

        public static IntPtr GetSelector(string name) => sel_registerName(name);

        public static IntPtr SendIntPtr(IntPtr receiver, IntPtr selector) => objc_msgSend(receiver, selector);

        public static void SendVoid_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1) =>
            objc_msgSend_Void_IntPtr(receiver, selector, arg1);

        public static IntPtr CreateNSString(string value)
        {
            value ??= string.Empty;

            var nsStringClass = GetClass("NSString");
            var stringWithUtf8Sel = GetSelector("stringWithUTF8String:");
            var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(value + "\0");

            var ptr = Marshal.AllocHGlobal(utf8Bytes.Length);
            Marshal.Copy(utf8Bytes, 0, ptr, utf8Bytes.Length);
            var nsString = objc_msgSend_IntPtr(nsStringClass, stringWithUtf8Sel, ptr);
            Marshal.FreeHGlobal(ptr);
            return nsString;
        }
    }
#endif
}

