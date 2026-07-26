using Newtonsoft.Json;
using SystemUptimeTracker.Api.Constants.Enums;

namespace SystemUptimeTracker.Api.Models.Authorization;

public class UserAccessControlPermission
{
    [JsonProperty]
    public string AccountId { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "PermissionName")]
    public string PermissionNameString { get; set; } = string.Empty;

    [JsonIgnore]
    public AccessControlPermission PermissionName
    {
        get
        {
            if (Enum.TryParse(PermissionNameString, out AccessControlPermission result))
            {
                return result;
            }

            return AccessControlPermission.UNKNOWN;
        }
    }

    [JsonProperty]
    public string PermissionType { get; set; } = string.Empty;

    [JsonProperty]
    public string TargetId { get; set; } = string.Empty;
}
