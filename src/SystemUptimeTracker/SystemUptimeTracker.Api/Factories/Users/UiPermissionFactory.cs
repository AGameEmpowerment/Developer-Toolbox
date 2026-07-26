using SystemUptimeTracker.Api.Factories.Users.Interfaces;
using SystemUptimeTracker.Api.Models.Authorization;
using SystemUptimeTracker.Api.Models.Ui.Permissions;

namespace SystemUptimeTracker.Api.Factories.Users;

public class UiPermissionFactory : IUiPermissionFactory
{
    public UiPermission? ToUi(UserAccessControlPermission? permission)
    {
        return permission == null
            ? null
            : new UiPermission
            {
                AccountId = permission.AccountId,
                Permission = permission.PermissionName,
                TargetId = permission.TargetId
            };
    }
}
