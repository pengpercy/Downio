#if WINDOWS
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Downio.Services.Notifications;

/// <summary>
/// Windows 10 and 11 toasts for an unpackaged app. Toasts are attributed through the AppUserModelID,
/// registered both on a Start menu shortcut (what Windows 10 requires) and under
/// HKCU\Software\Classes\AppUserModelId (display name and icon, honoured by Windows 10 1903+ and 11).
/// Windows 7/8/8.1 (reachable because the AOT build doesn't need an installed runtime) get a
/// notification-area balloon instead; Windows 8 and later turn it into a toast on their own.
/// Windows 10 also falls back to the balloon if a toast fails.
/// </summary>
internal static class WindowsSystemNotification
{
    private const string AppUserModelId = "pengpercy.Downio";
    private const int LegacyBalloonId = 1001;
    // Long enough for the default Windows 7 balloon time; removing the icon dismisses the balloon.
    private const int LegacyBalloonLifetimeMs = 10000;
    private static readonly object SyncRoot = new();
    private static bool _initialized;
    private static string? _logoPath;
    private static int _balloonGeneration;
    private static IntPtr _largeIcon;
    private static IntPtr _smallIcon;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppUserModelId));
            _initialized = true;

            // The shortcut, logo file and registry entry only exist for toasts.
            if (!OperatingSystem.IsWindowsVersionAtLeast(10)) return;

            _logoPath = ExtractLogo();
            TryRun(EnsureStartMenuShortcut, "Creating the Start menu shortcut for notifications failed");
            TryRun(RegisterAppUserModelId, "Registering the notification AppUserModelID failed");
        }
    }

    public static void ShowLegacyBalloon(string title, string message)
    {
        try
        {
            var data = CreateLegacyNotifyIconData(title, message);
            if (data.hWnd == IntPtr.Zero)
            {
                AppLog.Warn("No main window handle for the notification balloon.");
                return;
            }

            // A previous balloon's icon is still there for a while; replace it rather than stack a second one.
            var cleanup = CreateLegacyNotifyIconData(string.Empty, string.Empty);
            Shell_NotifyIcon(NIM_DELETE, ref cleanup);
            if (!Shell_NotifyIcon(NIM_ADD, ref data))
            {
                AppLog.Warn($"Adding the notification balloon failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            var generation = Interlocked.Increment(ref _balloonGeneration);
            _ = Task.Run(async () =>
            {
                await Task.Delay(LegacyBalloonLifetimeMs).ConfigureAwait(false);
                // A newer balloon owns the icon now and removes it itself.
                if (generation != Volatile.Read(ref _balloonGeneration)) return;
                var expired = CreateLegacyNotifyIconData(string.Empty, string.Empty);
                Shell_NotifyIcon(NIM_DELETE, ref expired);
            });
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Showing the notification balloon failed");
        }
    }

    public static bool TryShow(string title, string message)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
        {
            return false;
        }

        try
        {
            Initialize();

            var imageXml = _logoPath != null
                ? $@"<image placement=""appLogoOverride"" src=""{EscapeXml(new Uri(_logoPath).AbsoluteUri)}"" alt=""Downio""/>"
                : string.Empty;

            var xml = $"""
                       <toast>
                         <visual>
                           <binding template="ToastGeneric">
                             {imageXml}
                             <text>{EscapeXml(title)}</text>
                             <text>{EscapeXml(message)}</text>
                           </binding>
                         </visual>
                         <audio src="ms-winsoundevent:Notification.Default"/>
                       </toast>
                       """;

            var document = new XmlDocument();
            document.LoadXml(xml);

            var toast = new ToastNotification(document);
            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(toast);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Showing Windows toast failed");
            return false;
        }
    }

    private static void TryRun(Action action, string failureMessage)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, failureMessage);
        }
    }

    // The branding PNG is an embedded resource; toasts need a real file to show it.
    private static string? ExtractLogo()
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Downio");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "notification-logo.png");
            using (var source = AssetLoader.Open(new Uri("avares://Downio/Assets/Branding/app_icon.png")))
            using (var target = File.Create(path))
            {
                source.CopyTo(target);
            }
            return path;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Extracting the notification logo failed");
            return null;
        }
    }

    private static void RegisterAppUserModelId()
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}");
        key.SetValue("DisplayName", "Downio");
        if (_logoPath != null) key.SetValue("IconUri", _logoPath);
    }

    private static unsafe void EnsureStartMenuShortcut()
    {
        var programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(programsPath))
        {
            return;
        }

        var shortcutPath = Path.Combine(programsPath, "Downio.lnk");
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return;
        }

        // Plain vtable calls: NativeAOT can't activate [ComImport] coclasses (see TaskbarBadgeService).
        var classId = ShellLinkClassId;
        var shellLinkId = IShellLinkWId;
        Marshal.ThrowExceptionForHR(CoCreateInstance(&classId, IntPtr.Zero, 0x1 /* CLSCTX_INPROC_SERVER */,
            &shellLinkId, out var shellLink));
        var propertyStore = IntPtr.Zero;
        var persistFile = IntPtr.Zero;
        var appId = new PropVariant { ValueType = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(AppUserModelId) };
        try
        {
            CallString(shellLink, 20, exePath); // IShellLinkW::SetPath
            CallString(shellLink, 11, string.Empty); // SetArguments
            CallString(shellLink, 9, Path.GetDirectoryName(exePath)); // SetWorkingDirectory

            // The application icon is embedded in the executable.
            fixed (char* iconPath = exePath)
                Marshal.ThrowExceptionForHR(
                    ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)VTable(shellLink, 17))(shellLink, iconPath, 0));

            var propertyStoreId = IPropertyStoreId;
            Marshal.ThrowExceptionForHR(QueryInterface(shellLink, &propertyStoreId, out propertyStore));
            var appUserModelIdKey = PropertyKeys.AppUserModelId;
            Marshal.ThrowExceptionForHR( // IPropertyStore::SetValue, then Commit
                ((delegate* unmanaged[Stdcall]<IntPtr, PropertyKey*, PropVariant*, int>)VTable(propertyStore, 6))(
                    propertyStore, &appUserModelIdKey, &appId));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, int>)VTable(propertyStore, 7))(propertyStore));

            var persistFileId = IPersistFileId;
            Marshal.ThrowExceptionForHR(QueryInterface(shellLink, &persistFileId, out persistFile));
            fixed (char* path = shortcutPath)
                Marshal.ThrowExceptionForHR( // IPersistFile::Save
                    ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, int>)VTable(persistFile, 6))(persistFile, path, 1));
        }
        finally
        {
            Marshal.FreeCoTaskMem(appId.Pointer);
            if (persistFile != IntPtr.Zero) Release(persistFile);
            if (propertyStore != IntPtr.Zero) Release(propertyStore);
            Release(shellLink);
        }
    }

    private static string EscapeXml(string value) =>
        SecurityElement.Escape(value) ?? string.Empty;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static NotifyIconData CreateLegacyNotifyIconData(string title, string message)
    {
        LoadAppIcons();
        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            // Shell_NotifyIcon only accepts a window owned by this process. The main window
            // is only hidden when closed to the tray, so its handle stays valid.
            hWnd = MainWindowHandle(),
            uID = LegacyBalloonId,
            uFlags = NIF_ICON | NIF_TIP | NIF_INFO,
            hIcon = _smallIcon != IntPtr.Zero ? _smallIcon : LoadIcon(IntPtr.Zero, IDI_INFORMATION),
            szTip = "Downio",
            szInfoTitle = Truncate(title, 63),
            szInfo = Truncate(message, 255),
            // The app icon in the balloon, like the logo on a toast; the stock info icon if it couldn't be loaded.
            dwInfoFlags = _largeIcon != IntPtr.Zero ? NIIF_USER | NIIF_LARGE_ICON : NIIF_INFO,
            hBalloonIcon = _largeIcon,
            uTimeoutOrVersion = LegacyBalloonLifetimeMs
        };

        return data;
    }

    // Loaded once from the executable's embedded icon and kept for the life of the process.
    private static void LoadAppIcons()
    {
        if (_largeIcon != IntPtr.Zero || _smallIcon != IntPtr.Zero) return;
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath)) return;
        ExtractIconEx(exePath, 0, out _largeIcon, out _smallIcon, 1);
    }

    private static string Truncate(string value, int maxLength)
    {
        value ??= string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_USER = 0x00000004;
    private const uint NIIF_LARGE_ICON = 0x00000020;
    private const int IDI_INFORMATION = 32516;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    private static IntPtr MainWindowHandle() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            ? window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero
            : IntPtr.Zero;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, int lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid formatId, int propertyId)
    {
        public Guid FormatId = formatId;
        public int PropertyId = propertyId;
    }

    private static class PropertyKeys
    {
        public static PropertyKey AppUserModelId => new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    }

    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IShellLinkWId = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid IPropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid IPersistFileId = new("0000010B-0000-0000-C000-000000000046");

    // Only the VT_LPWSTR case is used; 24 bytes covers the x64 layout (16 on x86).
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort ValueType;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    private static unsafe void* VTable(IntPtr instance, int slot) => (*(void***)instance)[slot];

    private static unsafe int QueryInterface(IntPtr instance, Guid* interfaceId, out IntPtr result)
    {
        IntPtr value;
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)VTable(instance, 0))(instance, interfaceId, &value);
        result = hr < 0 ? IntPtr.Zero : value;
        return hr;
    }

    private static unsafe void Release(IntPtr instance) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(instance, 2))(instance);

    private static unsafe void CallString(IntPtr instance, int slot, string? value)
    {
        fixed (char* text = value)
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, char*, int>)VTable(instance, slot))(instance, text));
    }

    [DllImport("ole32.dll")]
    private static extern unsafe int CoCreateInstance(Guid* classId, IntPtr outer, uint context, Guid* interfaceId, out IntPtr instance);
}
#endif
