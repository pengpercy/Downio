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
        MacProcessName.TrySet("Downio");

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
                // Keep the standard macOS app menu entries such as Services, Hide,
                // Hide Others and Show All in the application menu.
                DisableDefaultApplicationMenuItems = false,
                // Let Avalonia set the macOS process name so standard menu items
                // use "Downio" instead of the fallback Avalonia application name.
                DisableSetProcessName = false,
                DisableAvaloniaAppDelegate = false 
            });
}
