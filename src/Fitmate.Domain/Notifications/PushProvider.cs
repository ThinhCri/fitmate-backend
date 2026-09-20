namespace Fitmate.Domain.Notifications;

/// <summary>
/// BE-200 push provider. Independent from <see cref="DevicePlatform"/>: a
/// single device may register multiple times under different providers (rare
/// but allowed), and the backend must NOT auto-detect the provider from the
/// raw token or from <see cref="DevicePlatform"/>. The client always sends
/// the provider explicitly. See DEC-044.
/// </summary>
public enum PushProvider
{
    /// <summary>Firebase Cloud Messaging. Requires service-account credentials at the adapter.</summary>
    Fcm = 0,

    /// <summary>Expo Push Token (<c>ExponentPushToken[...]</c>). Free tier; no Google config required.</summary>
    Expo = 1,
}
