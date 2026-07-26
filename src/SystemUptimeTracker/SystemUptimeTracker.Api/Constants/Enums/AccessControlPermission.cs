namespace SystemUptimeTracker.Api.Constants.Enums;

/// <summary>
/// Defines the minimal administrative permissions retained by the starter application.
/// </summary>
public enum AccessControlPermission
{
    CAN_IMPERSONATE,
    CAN_ADMIN_APPLICATION,
    UNKNOWN = -1,
}
