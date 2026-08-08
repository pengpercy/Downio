using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

#if MACOS
using Foundation;
using UserNotifications;
#endif

namespace Downio.Services.Notifications;

#if MACOS
[SupportedOSPlatform("macos12.0")]
#endif
public static class MacSystemNotification
{
#if MACOS
    private static readonly SemaphoreSlim AuthorizationGate = new(1, 1);
    private static readonly NotificationCenterDelegate NotificationDelegate = new();
    private static readonly LegacyNotificationCenterDelegate LegacyNotificationDelegate = new();
#endif

    public static void Initialize()
    {
#if MACOS
        UNUserNotificationCenter.Current.Delegate = NotificationDelegate;
#pragma warning disable CA1422, CS0618
        NSUserNotificationCenter.DefaultUserNotificationCenter.Delegate = LegacyNotificationDelegate;
#pragma warning restore CA1422, CS0618
#endif
    }

    public static async Task<bool> TryShowAsync(string title, string message)
    {
#if !MACOS
        await Task.CompletedTask;
        return false;
#else
        try
        {
            var center = UNUserNotificationCenter.Current;
            center.Delegate = NotificationDelegate;

            if (!await EnsureAuthorizationAsync(center).ConfigureAwait(false))
            {
                return TryShowLegacy(title, message);
            }

            using var content = new UNMutableNotificationContent
            {
                Title = title ?? string.Empty,
                Body = message ?? string.Empty,
                Sound = UNNotificationSound.Default
            };
            using var request = UNNotificationRequest.FromIdentifier(
                Guid.NewGuid().ToString("N"),
                content,
                trigger: null);

            await center.AddNotificationRequestAsync(request).ConfigureAwait(false);
            AppLog.Info("Modern macOS system notification delivered.");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Modern macOS notification delivery failed, using legacy fallback: {ex.Message}");
            return TryShowLegacy(title, message);
        }
#endif
    }

#if MACOS
    private static async Task<bool> EnsureAuthorizationAsync(UNUserNotificationCenter center)
    {
        await AuthorizationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = await center.GetNotificationSettingsAsync().ConfigureAwait(false);
            switch (settings.AuthorizationStatus)
            {
                case UNAuthorizationStatus.Authorized:
                case UNAuthorizationStatus.Provisional:
                    return true;
                case UNAuthorizationStatus.Denied:
                    LogUnavailableSettings(settings);
                    return false;
                case UNAuthorizationStatus.NotDetermined:
                    return await RequestAuthorizationAsync(center).ConfigureAwait(false);
                default:
                    LogUnavailableSettings(settings);
                    return false;
            }
        }
        finally
        {
            AuthorizationGate.Release();
        }
    }

    private static void LogUnavailableSettings(UNNotificationSettings settings)
    {
        AppLog.Warn(
            $"macOS notification settings unavailable: authorization={settings.AuthorizationStatus}, " +
            $"alert={settings.AlertSetting}, center={settings.NotificationCenterSetting}, sound={settings.SoundSetting}");
    }

    private static bool TryShowLegacy(string title, string message)
    {
        try
        {
#pragma warning disable CA1422, CS0618
            using var notification = new NSUserNotification
            {
                Title = title ?? string.Empty,
                InformativeText = message ?? string.Empty,
                SoundName = NSUserNotification.NSUserNotificationDefaultSoundName
            };
            var center = NSUserNotificationCenter.DefaultUserNotificationCenter;
            center.Delegate = LegacyNotificationDelegate;
            center.DeliverNotification(notification);
#pragma warning restore CA1422, CS0618
            AppLog.Info("Legacy macOS system notification delivered.");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Legacy macOS notification delivery failed");
            return false;
        }
    }

    private static async Task<bool> RequestAuthorizationAsync(UNUserNotificationCenter center)
    {
        var result = await center.RequestAuthorizationAsync(
                UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound)
            .ConfigureAwait(false);

        return result.Item1 && result.Item2 is null;
    }

    private sealed class NotificationCenterDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(
            UNUserNotificationCenter center,
            UNNotification notification,
            Action<UNNotificationPresentationOptions> completionHandler)
        {
            completionHandler(
                UNNotificationPresentationOptions.Banner |
                UNNotificationPresentationOptions.List |
                UNNotificationPresentationOptions.Sound);
        }
    }

#pragma warning disable CA1422, CS0618
    private sealed class LegacyNotificationCenterDelegate : NSUserNotificationCenterDelegate
    {
        public override bool ShouldPresentNotification(
            NSUserNotificationCenter center,
            NSUserNotification notification) => true;
    }
#pragma warning restore CA1422, CS0618
#endif
}
