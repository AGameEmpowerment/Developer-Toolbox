namespace SystemUptimeTracker.Api.Models.ApplicationSettings;

/// <summary>
/// Configures ASP.NET Core Data Protection for cookie and token persistence.
/// </summary>
public sealed class DataProtectionSettings
{
    /// <summary>
    /// Gets or sets the logical application name used to isolate the key ring.
    /// </summary>
    public string ApplicationName { get; set; } = "SystemUptimeTracker";

    /// <summary>
    /// Gets or sets the optional file-system path used to persist the key ring.
    /// </summary>
    public string KeyRingPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether Windows DPAPI should protect the persisted keys at rest.
    /// </summary>
    public bool ProtectKeysWithDpapi { get; set; } = true;
}
