using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
#if WINDOWS
using System.Drawing.Imaging;
#endif

namespace Downio.Helpers;

/// <summary>
/// Looks up the icon the platform's file manager shows for a file extension
/// (Finder on macOS, Explorer on Windows). Results are cached per extension;
/// null means no system icon is available and callers should fall back to their own glyph.
/// </summary>
public static class SystemFileIcon
{
    private const int IconPixelSize = 64;
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? ForExtension(string? extension)
    {
        var ext = (extension ?? string.Empty).Trim().TrimStart('.');
        if (ext.Length == 0) return null;
        if (Cache.TryGetValue(ext, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            var png = OperatingSystem.IsMacOS() ? MacIconPng(ext)
                : OperatingSystem.IsWindows() ? WindowsIconPng(ext)
                : null;
            if (png is { Length: > 0 }) bitmap = new Bitmap(new MemoryStream(png));
        }
        catch (Exception ex)
        {
            Services.AppLog.Warn($"System icon lookup failed for .{ext}: {ex.Message}");
        }

        Cache[ext] = bitmap;
        return bitmap;
    }

    #region macOS

    private const string ObjcLib = "/usr/lib/libobjc.A.dylib";
    private const nuint NSBitmapImageFileTypePNG = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

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
    private static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg1);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendRect(IntPtr receiver, IntPtr selector, ref CGRect rect, IntPtr arg2, IntPtr arg3);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendNuint(IntPtr receiver, IntPtr selector, nuint arg1, IntPtr arg2);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern nuint SendReturnNuint(IntPtr receiver, IntPtr selector);

    private static byte[]? MacIconPng(string extension)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            var fileType = SendUtf8(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), extension);
            var image = Send(workspace, sel_registerName("iconForFileType:"), fileType);
            if (image == IntPtr.Zero) return null;

            var rect = new CGRect { Width = IconPixelSize, Height = IconPixelSize };
            var cgImage = SendRect(image, sel_registerName("CGImageForProposedRect:context:hints:"), ref rect, IntPtr.Zero, IntPtr.Zero);
            if (cgImage == IntPtr.Zero) return null;

            var rep = Send(Send(objc_getClass("NSBitmapImageRep"), sel_registerName("alloc")),
                sel_registerName("initWithCGImage:"), cgImage);
            if (rep == IntPtr.Zero) return null;
            try
            {
                var properties = Send(objc_getClass("NSDictionary"), sel_registerName("dictionary"));
                var data = SendNuint(rep, sel_registerName("representationUsingType:properties:"), NSBitmapImageFileTypePNG, properties);
                if (data == IntPtr.Zero) return null;

                var length = (int)SendReturnNuint(data, sel_registerName("length"));
                var bytes = Send(data, sel_registerName("bytes"));
                if (length <= 0 || bytes == IntPtr.Zero) return null;

                var png = new byte[length];
                Marshal.Copy(bytes, png, 0, length);
                return png;
            }
            finally
            {
                Send(rep, sel_registerName("release"));
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    #endregion

    #region Windows

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static byte[]? WindowsIconPng(string extension)
    {
#if WINDOWS
        // With SHGFI_USEFILEATTRIBUTES the file doesn't need to exist; only the extension matters.
        var info = new SHFILEINFO();
        SHGetFileInfo("file." + extension, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(info.hIcon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
#else
        _ = extension;
        return null;
#endif
    }

    #endregion
}
