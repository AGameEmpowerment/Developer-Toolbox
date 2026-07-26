using SystemUptimeTracker.Api.Constants.Enums;

namespace SystemUptimeTracker.Api.Models.Ui.Permissions;

public class UiPermission
{
    public string AccountId { get; set; } = string.Empty;
    public AccessControlPermission Permission { get; set; }
    public string TargetId { get; set; } = string.Empty;
}
