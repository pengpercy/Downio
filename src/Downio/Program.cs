using Avalonia;
using System;
using System.Threading.Tasks;
using Downio.Helpers;
using Downio.Services;

namespace Downio;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // MacProcessName.TrySet("Downio");

        // Must run before Avalonia brings up AppKit so native panels pick up the app language.
        MacAppLanguage.Apply(new SettingsService().Settings.Language);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                AppLog.Error(ex, "Unhandled app domain exception");
            }
            else
            {
                AppLog.Error($"Unhandled app domain exception: {e.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Fatal startup exception");
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .With(new MacOSPlatformOptions
            {
                // Avalonia's standard app menu items (Hide / Hide Others / Show All / Quit) are
                // English-only; App.axaml supplies localized replacements instead.
                DisableDefaultApplicationMenuItems = true,
                DisableSetProcessName = true,
                DisableAvaloniaAppDelegate = false
            })
#pragma warning disable AVALONIA_X11_CSD
            // Without this, X11 ignores ExtendClientAreaToDecorationsHint and the window manager
            // adds its own title bar above ours
            .With(new X11PlatformOptions { EnableDrawnDecorations = LinuxDesktop.UsesDrawnDecorations })
#pragma warning restore AVALONIA_X11_CSD
            .With(Win32Options());

    // Windows 7 has neither WaitOnAddress (needed by ANGLE's av_libglesv2.dll) nor DirectComposition,
    // so the Win7 package renders in software into a plain redirection surface.
    private static Win32PlatformOptions Win32Options() =>
        OperatingSystem.IsWindows() && !OperatingSystem.IsWindowsVersionAtLeast(6, 2)
            ? new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
                CompositionMode = [Win32CompositionMode.RedirectionSurface]
            }
            : new Win32PlatformOptions();
}
