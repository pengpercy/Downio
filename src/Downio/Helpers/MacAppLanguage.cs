using System;
using System.Runtime.InteropServices;

namespace Downio.Helpers;

/// <summary>
/// Makes native macOS UI (open/save panels, standard menu items) follow the app's language setting.
/// AppKit resolves the bundle's localization from the AppleLanguages preference the first time it
/// is needed, so <see cref="Apply"/> has to run before Avalonia initializes AppKit; later changes
/// only take effect on the next launch.
/// </summary>
internal static class MacAppLanguage
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint kCFStringEncodingUTF8 = 0x08000100;

    public static void Apply(string? appLanguage)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // "System" (or anything unknown) removes the app-level override so macOS uses the system order.
        var appleLanguage = appLanguage switch
        {
            "zh-CN" => "zh-Hans",
            "en-US" => "en",
            _ => null
        };

        var key = IntPtr.Zero;
        var app = IntPtr.Zero;
        var language = IntPtr.Zero;
        var languages = IntPtr.Zero;
        try
        {
            key = CFStringCreateWithCString(IntPtr.Zero, "AppleLanguages", kCFStringEncodingUTF8);
            // The value of the kCFPreferencesCurrentApplication constant is this literal string.
            app = CFStringCreateWithCString(IntPtr.Zero, "kCFPreferencesCurrentApplication", kCFStringEncodingUTF8);

            if (appleLanguage != null)
            {
                language = CFStringCreateWithCString(IntPtr.Zero, appleLanguage, kCFStringEncodingUTF8);
                var callbacks = NativeLibrary.GetExport(NativeLibrary.Load(CoreFoundation), "kCFTypeArrayCallBacks");
                var values = new[] { language };
                languages = CFArrayCreate(IntPtr.Zero, values, 1, callbacks);
            }

            CFPreferencesSetAppValue(key, languages, app);
            CFPreferencesAppSynchronize(app);
        }
        catch
        {
            // Native panels simply keep the system language.
        }
        finally
        {
            if (languages != IntPtr.Zero) CFRelease(languages);
            if (language != IntPtr.Zero) CFRelease(language);
            if (app != IntPtr.Zero) CFRelease(app);
            if (key != IntPtr.Zero) CFRelease(key);
        }
    }

    [DllImport(CoreFoundation, CharSet = CharSet.Ansi)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayCreate(IntPtr alloc, IntPtr[] values, nint numValues, IntPtr callBacks);

    [DllImport(CoreFoundation)]
    private static extern void CFPreferencesSetAppValue(IntPtr key, IntPtr value, IntPtr applicationId);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFPreferencesAppSynchronize(IntPtr applicationId);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
