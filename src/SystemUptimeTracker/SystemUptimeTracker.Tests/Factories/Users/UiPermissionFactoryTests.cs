using SystemUptimeTracker.Api.Factories.Users;
using SystemUptimeTracker.Api.Models.Authorization;
using SystemUptimeTracker.Api.Models.Ui.Permissions;

namespace SystemUptimeTracker.Tests.Factories.Users;

[TestFixture(Category = "Unit")]
public class UiPermissionFactoryTests
{
    private UiPermissionFactory _factory;

    [SetUp]
    public void Setup()
    {
        _factory = new UiPermissionFactory();
    }

    [Test]
    public void ToUiReturnsNullWhenUserAccessControlPermissionIsNull()
    {
        UiPermission? result = _factory.ToUi(null);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ToUiSetsValues()
    {
        UserAccessControlPermission userAccessControlPermission = new UserAccessControlPermission()
        {
            AccountId = "ABC",
            PermissionType = "bob",
            TargetId = "22"
        };
        UiPermission? result = _factory.ToUi(userAccessControlPermission);
        Assert.That(result, Is.Not.Null);
        UiPermission actual = result!;
        Assert.That(actual.AccountId, Is.EqualTo(userAccessControlPermission.AccountId));
        Assert.That(actual.Permission, Is.EqualTo(userAccessControlPermission.PermissionName));
        Assert.That(actual.TargetId, Is.EqualTo(userAccessControlPermission.TargetId));
    }
}
