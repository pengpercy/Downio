#if WINDOWS
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Downio.Services.Notifications;

internal static class WindowsSystemNotification
{
    private const string AppUserModelId = "pengpercy.Downio";
    private const int LegacyBalloonId = 1001;
    private static readonly object SyncRoot = new();
    private static bool _initialized;

    public static void Initialize()
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppUserModelId));
            EnsureStartMenuShortcut();
            _initialized = true;
        }
    }

    public static void ShowLegacyBalloon(string title, string message)
    {
        try
        {
            var data = CreateLegacyNotifyIconData(title, message);
            Shell_NotifyIcon(NIM_DELETE, ref data);

            if (!Shell_NotifyIcon(NIM_ADD, ref data))
            {
                return;
            }

            Shell_NotifyIcon(NIM_MODIFY, ref data);

            _ = Task.Run(async () =>
            {
                await Task.Delay(6000).ConfigureAwait(false);
                var cleanup = CreateLegacyNotifyIconData(string.Empty, string.Empty);
                Shell_NotifyIcon(NIM_DELETE, ref cleanup);
            });
        }
        catch
        {
        }
    }

    public static bool TryShow(string title, string message)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
        {
            return false;
        }

        try
        {
            Initialize();

            var appLogoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "app_icon.png");
            var imageXml = File.Exists(appLogoPath)
                ? $@"<image placement=""appLogoOverride"" hint-crop=""circle"" src=""{EscapeXml(new Uri(appLogoPath).AbsoluteUri)}"" alt=""Downio""/>"
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
                       </toast>
                       """;

            var document = new XmlDocument();
            document.LoadXml(xml);

            var toast = new ToastNotification(document);
            ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(toast);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureStartMenuShortcut()
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

        var shellLink = (IShellLinkW)new ShellLink();
        try
        {
            Marshal.ThrowExceptionForHR(shellLink.SetPath(exePath));
            Marshal.ThrowExceptionForHR(shellLink.SetArguments(string.Empty));
            Marshal.ThrowExceptionForHR(shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath)));

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "app_ico.ico");
            if (File.Exists(iconPath))
            {
                Marshal.ThrowExceptionForHR(shellLink.SetIconLocation(iconPath, 0));
            }

            var propertyStore = (IPropertyStore)shellLink;
            using var appId = new PropVariant(AppUserModelId);
            var appUserModelIdKey = PropertyKeys.AppUserModelId;
            Marshal.ThrowExceptionForHR(propertyStore.SetValue(ref appUserModelIdKey, appId));
            Marshal.ThrowExceptionForHR(propertyStore.Commit());

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(shellLink);
        }
    }

    private static string EscapeXml(string value) =>
        SecurityElement.Escape(value) ?? string.Empty;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static NotifyIconData CreateLegacyNotifyIconData(string title, string message)
    {
        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = GetDesktopWindow(),
            uID = LegacyBalloonId,
            uFlags = NIF_ICON | NIF_TIP | NIF_INFO,
            hIcon = LoadIcon(IntPtr.Zero, IDI_INFORMATION),
            szTip = "Downio",
            szInfoTitle = Truncate(title, 63),
            szInfo = Truncate(message, 255),
            dwInfoFlags = NIIF_INFO,
            uTimeoutOrVersion = 5000
        };

        return data;
    }

    private static string Truncate(string value, int maxLength)
    {
        value ??= string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIIF_INFO = 0x00000001;
    private const int IDI_INFORMATION = 32516;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, int lpIconName);

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

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        int GetPath([Out] char[] pszFile, int cch, out WIN32_FIND_DATAW pfd, uint fFlags);
        int GetIDList(out IntPtr ppidl);
        int SetIDList(IntPtr pidl);
        int GetDescription([Out] char[] pszName, int cch);
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        int GetWorkingDirectory([Out] char[] pszDir, int cch);
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string? pszDir);
        int GetArguments([Out] char[] pszArgs, int cch);
        int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        int GetHotkey(out short pwHotkey);
        int SetHotkey(short wHotkey);
        int GetShowCmd(out int piShowCmd);
        int SetShowCmd(int iShowCmd);
        int GetIconLocation([Out] char[] pszIconPath, int cch, out int piIcon);
        int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        int Resolve(IntPtr hwnd, uint fFlags);
        int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        int GetCount(out uint cProps);
        int GetAt(uint iProp, out PropertyKey pkey);
        int GetValue(ref PropertyKey key, out PropVariant pv);
        int SetValue(ref PropertyKey key, PropVariant pv);
        int Commit();
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [StructLayout(LayoutKind.Explicit)]
    private sealed class PropVariant : IDisposable
    {
        [FieldOffset(0)]
        private ushort valueType;
        [FieldOffset(8)]
        private IntPtr pointerValue;

        public PropVariant(string value)
        {
            valueType = 31;
            pointerValue = Marshal.StringToCoTaskMemUni(value);
        }

        public void Dispose()
        {
            PropVariantClear(this);
            GC.SuppressFinalize(this);
        }

        ~PropVariant()
        {
            Dispose();
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear([In, Out] PropVariant pvar);
    }
}
#endif
