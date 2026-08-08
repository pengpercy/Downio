using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

#if MACOS
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
#endif

    public static void Initialize()
    {
#if MACOS
        UNUserNotificationCenter.Current.Delegate = NotificationDelegate;
#endif
    }

    public static async Task<bool> TryShowAsync(string title, string message)
    {
#if !MACOS
        await Task.CompletedTask;
        return false;
#else
        var center = UNUserNotificationCenter.Current;
        center.Delegate = NotificationDelegate;

        if (!await EnsureAuthorizationAsync(center).ConfigureAwait(false))
        {
            return false;
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
        return true;
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
                    return false;
                case UNAuthorizationStatus.NotDetermined:
                    return await RequestAuthorizationAsync(center).ConfigureAwait(false);
                default:
                    return false;
            }
        }
        finally
        {
            AuthorizationGate.Release();
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
#endif
}
