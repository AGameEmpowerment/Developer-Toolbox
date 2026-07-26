using System.Collections.Generic;
using SystemUptimeTracker.Qa.Automation.Support;
using NUnit.Framework;

namespace SystemUptimeTracker.Qa.Automation.Infrastructure;

internal static class TestCredentialGuard
{
    private static readonly HashSet<string> _placeholderValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "**Username**",
        "**Password**",
        "test-user",
        "test-password",
        "__SET_IN_USER_SECRETS_OR_ENV__"
    };

    public static void RequireConfigured(LoginCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (!HasUsableValue(credentials.Username) || !HasUsableValue(credentials.Password))
        {
            Assert.Ignore("Microsoft login credentials are not configured. Set AppSettings:Credentials with user secrets or environment variables before running authenticated UI tests.");
        }
    }

    private static bool HasUsableValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !_placeholderValues.Contains(value);
    }
}
