#if WINDOWS
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Downio.Services.Notifications;

internal static class WindowsSystemNotification
{
    private const string AppUserModelId = "pengpercy.Downio";
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

            EnsureStartMenuShortcut();
            ToastNotificationManagerCompat.OnActivated += _ => { };
            _initialized = true;
        }
    }

    public static void Show(string title, string message)
    {
        Initialize();

        var builder = new ToastContentBuilder()
            .AddArgument("action", "view")
            .AddText(title)
            .AddText(message);

        var appLogoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", "app_icon.png");
        if (File.Exists(appLogoPath))
        {
            builder.AddAppLogoOverride(new Uri(appLogoPath));
        }

        builder.Show();
    }

    private static void EnsureStartMenuShortcut()
    {
        var programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(programsPath))
        {
            return;
        }

        var shortcutPath = Path.Combine(programsPath, "Downio.lnk");
        if (File.Exists(shortcutPath))
        {
            return;
        }

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
