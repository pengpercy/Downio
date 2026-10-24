using System;
using System.Runtime.InteropServices;

namespace Downio.Services.Notifications;

/// <summary>
/// macOS notifications. macOS 10.14+ uses UNUserNotificationCenter, which needs a bundle
/// identifier (the packaged Downio.app) and the user's permission, requested once at startup.
/// NSUserNotificationCenter, deprecated since macOS 11 and often silent on current releases,
/// is only used on 10.13; anything else returns false so the caller can fall back to osascript.
/// </summary>
public static unsafe class MacSystemNotification
{
#if MACOS
    private const string ObjcLib = "/usr/lib/libobjc.A.dylib";
    private const string UserNotificationsFramework = "/System/Library/Frameworks/UserNotifications.framework/UserNotifications";
    private const nuint UNAuthorizationOptionBadge = 1 << 0;
    private const nuint UNAuthorizationOptionSound = 1 << 1;
    private const nuint UNAuthorizationOptionAlert = 1 << 2;
    private const int BlockIsGlobal = 1 << 28;

    private static readonly Lazy<bool> CanUseUserNotifications = new(DetectUserNotifications);
    private static BlockLiteral* _authorizationHandler;
#endif

    /// <summary>Asks for notification permission up front so the prompt doesn't wait for the first finished download.</summary>
    public static void Initialize()
    {
#if MACOS
        if (!CanUseUserNotifications.Value) return;
        try
        {
            var pool = objc_autoreleasePoolPush();
            try
            {
                var center = Send(Class("UNUserNotificationCenter"), Sel("currentNotificationCenter"));
                SendOptionsBlock(center, Sel("requestAuthorizationWithOptions:completionHandler:"),
                    // Badge too: without it macOS may also hide the download-speed label on the Dock icon.
                    UNAuthorizationOptionAlert | UNAuthorizationOptionSound | UNAuthorizationOptionBadge, AuthorizationHandler());
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Requesting macOS notification permission failed");
        }
#endif
    }

    public static bool TryShow(string title, string message)
    {
#if !MACOS
        return false;
#else
        try
        {
            var pool = objc_autoreleasePoolPush();
            try
            {
                if (CanUseUserNotifications.Value) return ShowUserNotification(title, message);
                return !OperatingSystem.IsMacOSVersionAtLeast(10, 14) && ShowLegacyNotification(title, message);
            }
            finally
            {
                objc_autoreleasePoolPop(pool);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Showing macOS notification failed");
            return false;
        }
#endif
    }

#if MACOS
    private static bool DetectUserNotifications()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(10, 14)) return false;
        try
        {
            // currentNotificationCenter throws an Objective-C exception (and takes the process
            // down) when the executable isn't inside an app bundle, e.g. under `dotnet run`.
            var bundle = Send(Class("NSBundle"), Sel("mainBundle"));
            if (bundle == IntPtr.Zero || Send(bundle, Sel("bundleIdentifier")) == IntPtr.Zero) return false;
            NativeLibrary.Load(UserNotificationsFramework);
            return Class("UNUserNotificationCenter") != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShowUserNotification(string title, string message)
    {
        var center = Send(Class("UNUserNotificationCenter"), Sel("currentNotificationCenter"));
        if (center == IntPtr.Zero) return false;

        var content = Send(Send(Class("UNMutableNotificationContent"), Sel("alloc")), Sel("init"));
        if (content == IntPtr.Zero) return false;
        try
        {
            Send(content, Sel("setTitle:"), NSString(title));
            Send(content, Sel("setBody:"), NSString(message));
            Send(content, Sel("setSound:"), Send(Class("UNNotificationSound"), Sel("defaultSound")));

            // A nil trigger delivers immediately.
            var request = Send(Class("UNNotificationRequest"), Sel("requestWithIdentifier:content:trigger:"),
                NSString(Guid.NewGuid().ToString()), content, IntPtr.Zero);
            if (request == IntPtr.Zero) return false;

            Send(center, Sel("addNotificationRequest:withCompletionHandler:"), request, IntPtr.Zero);
            return true;
        }
        finally
        {
            Send(content, Sel("release"));
        }
    }

    private static bool ShowLegacyNotification(string title, string message)
    {
        var center = Send(Class("NSUserNotificationCenter"), Sel("defaultUserNotificationCenter"));
        if (center == IntPtr.Zero) return false;

        var notification = Send(Send(Class("NSUserNotification"), Sel("alloc")), Sel("init"));
        if (notification == IntPtr.Zero) return false;
        try
        {
            Send(notification, Sel("setTitle:"), NSString(title));
            Send(notification, Sel("setInformativeText:"), NSString(message));
            Send(center, Sel("deliverNotification:"), notification);
            return true;
        }
        finally
        {
            Send(notification, Sel("release"));
        }
    }

    // A capture-less global block, built once and never freed, for the permission callback.
    private static BlockLiteral* AuthorizationHandler()
    {
        if (_authorizationHandler != null) return _authorizationHandler;

        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (nuint)sizeof(BlockLiteral);

        var block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        block->Isa = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_NSConcreteGlobalBlock");
        block->Flags = BlockIsGlobal;
        block->Invoke = (IntPtr)(delegate* unmanaged<IntPtr, byte, IntPtr, void>)&OnAuthorizationResult;
        block->Descriptor = descriptor;
        return _authorizationHandler = block;
    }

    [UnmanagedCallersOnly]
    private static void OnAuthorizationResult(IntPtr block, byte granted, IntPtr error)
    {
        if (granted != 0) return;
        AppLog.Warn(error == IntPtr.Zero
            ? "macOS notification permission was not granted; system notifications are off."
            : $"macOS notification permission request failed: {Describe(error)}");
    }

    private static string Describe(IntPtr error)
    {
        var domain = Marshal.PtrToStringUTF8(Send(Send(error, Sel("domain")), Sel("UTF8String")));
        var code = (long)Send(error, Sel("code"));
        var text = Marshal.PtrToStringUTF8(Send(Send(error, Sel("localizedDescription")), Sel("UTF8String")));
        return $"{domain} {code}: {text}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public BlockDescriptor* Descriptor;
    }

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Sel(string name) => sel_registerName(name);

    private static IntPtr NSString(string value) =>
        SendUtf8(Class("NSString"), Sel("stringWithUTF8String:"), value ?? string.Empty);

    [DllImport(ObjcLib)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjcLib)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjcLib)]
    private static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjcLib)]
    private static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg1);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern void SendOptionsBlock(IntPtr receiver, IntPtr selector, nuint options, BlockLiteral* block);
#endif
}
