using System.Diagnostics.CodeAnalysis;

namespace SystemUptimeTracker.Api.Constants;

[ExcludeFromCodeCoverage]
public static class SystemUptimeTrackerAuthenticationSchemes
{
    public const string APPLICATION = "SystemUptimeTracker.Application";
    public const string ANTIFORGERY_HEADER_NAME = "X-CSRF-TOKEN";
}
