namespace SystemUptimeTracker.Api.Constants;

public static class ApplicationRoleNames
{
    public const string ADMIN = "Admin";
    public const string MANAGER = "Manager";
    public const string CONTRIBUTOR = "Contributor";
    public const string READ = "Read";

    public static readonly string[] All =
    [
        ADMIN,
        MANAGER,
        CONTRIBUTOR,
        READ
    ];
}
