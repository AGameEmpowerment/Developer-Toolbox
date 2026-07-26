using SystemUptimeTracker.Api.Models.Authorization;
using SystemUptimeTracker.Api.Models.Ui.Permissions;

namespace SystemUptimeTracker.Api.Factories.Users.Interfaces;

public interface IUiPermissionFactory
{
    UiPermission? ToUi(UserAccessControlPermission? permission);
}
