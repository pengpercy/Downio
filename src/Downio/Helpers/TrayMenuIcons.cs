using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Downio.Helpers;

/// <summary>
/// Gives the Windows tray menu its icons. The tray menu is built from <see cref="NativeMenuItem"/>s,
/// whose icon can only be a bitmap, so the app's vector icons are rendered into bitmaps in the
/// current theme's color and re-rendered when the theme changes. macOS and Linux keep the system
/// look of their native menus and are left alone.
/// </summary>
public static class TrayMenuIcons
{
    /// <summary>Resource key of the <see cref="Geometry"/> drawn as the item's icon (24×24 grid).</summary>
    public static readonly AttachedProperty<string?> GlyphProperty =
        AvaloniaProperty.RegisterAttached<NativeMenuItem, string?>("Glyph", typeof(TrayMenuIcons));

    /// <summary>When true, the item shows the app logo instead of a glyph.</summary>
    public static readonly AttachedProperty<bool> IsLogoProperty =
        AvaloniaProperty.RegisterAttached<NativeMenuItem, bool>("IsLogo", typeof(TrayMenuIcons));

    public static string? GetGlyph(NativeMenuItem item) => item.GetValue(GlyphProperty);
    public static void SetGlyph(NativeMenuItem item, string? value) => item.SetValue(GlyphProperty, value);
    public static bool GetIsLogo(NativeMenuItem item) => item.GetValue(IsLogoProperty);
    public static void SetIsLogo(NativeMenuItem item, bool value) => item.SetValue(IsLogoProperty, value);

    // Rendered at 3× the 16px slot so the icon stays crisp up to 300% display scaling.
    private const int BitmapSize = 48;
    private const double GlyphGrid = 24;

    public static void Attach(Application app, NativeMenu? menu)
    {
        if (!OperatingSystem.IsWindows() || menu is null)
        {
            return;
        }

        Bitmap? logo = null;
        try
        {
            logo = new Bitmap(AssetLoader.Open(new Uri("avares://Downio/Assets/Branding/app_icon.png")));
        }
        catch (Exception)
        {
            // A missing logo just leaves the header without an icon.
        }

        void Apply()
        {
            var brush = app.TryGetResource("TrayMenuIconBrush", app.ActualThemeVariant, out var value)
                        && value is IBrush b
                ? b
                : Brushes.Gray;

            foreach (var item in menu.Items)
            {
                if (item is not NativeMenuItem nativeItem)
                {
                    continue;
                }

                if (GetIsLogo(nativeItem))
                {
                    nativeItem.Icon = logo;
                    continue;
                }

                if (GetGlyph(nativeItem) is { } key
                    && app.TryGetResource(key, app.ActualThemeVariant, out var g)
                    && g is Geometry geometry)
                {
                    var old = nativeItem.Icon;
                    nativeItem.Icon = Render(geometry, brush);
                    old?.Dispose();
                }
            }
        }

        Apply();
        app.ActualThemeVariantChanged += (_, _) => Apply();
    }

    private static Bitmap Render(Geometry geometry, IBrush brush)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(BitmapSize, BitmapSize), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var scale = BitmapSize / GlyphGrid;
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            {
                context.DrawGeometry(brush, null, geometry);
            }
        }

        return bitmap;
    }
}
