using Microsoft.Extensions.Logging;
using SystemUptimeTracker.Api;
using SystemUptimeTracker.Api.Helpers.Validators;
using SystemUptimeTracker.Api.Models.ApplicationSettings;

namespace SystemUptimeTracker.Tests.Helpers.Validators;

[TestFixture(Category = "Unit")]
public class AppSettingsOptionsValidatorTests
{
    [Test]
    public void Validate_WhenRedisLocalOverrideIsEnabled_AllowsMissingRedisEndpoint()
    {
        var validator = new AppSettingsOptionsValidator();
        AppSettings appSettings = CreateValidAppSettings();
        appSettings.Redis = new Redis
        {
            LocalOverride = true
        };

        var result = validator.Validate(appSettings);

        Assert.That(result.IsValid, Is.True, string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));
    }

    [Test]
    public void Validate_WhenRedisEndpointIsMissing_TreatsCacheAsInMemoryFallback()
    {
        var validator = new AppSettingsOptionsValidator();
        AppSettings appSettings = CreateValidAppSettings();
        appSettings.Redis = new Redis
        {
            InstanceName = "SystemUptimeTracker_localhost",
            LocalOverride = false,
            Url = string.Empty
        };

        var result = validator.Validate(appSettings);

        Assert.That(result.IsValid, Is.True, string.Join("; ", result.Errors.Select(error => error.ErrorMessage)));
    }

    [Test]
    public void Validate_WhenRedisUrlIsConfiguredWithoutInstanceName_ReturnsRedisConfigurationError()
    {
        var validator = new AppSettingsOptionsValidator();
        AppSettings appSettings = CreateValidAppSettings();
        appSettings.Redis = new Redis
        {
            InstanceName = string.Empty,
            LocalOverride = false,
            Url = "localhost:10120"
        };

        var result = validator.Validate(appSettings);

        Assert.That(result.Errors.Select(error => error.PropertyName), Does.Contain("Redis.InstanceName"));
    }

    [Test]
    public void ResolveRedactionKeyMaterial_WhenPlaceholderValueIsConfigured_GeneratesValidBase64Key()
    {
        string resolvedKey = RegisterDependentServices.ResolveRedactionKeyMaterial("Replace-Key-From-Secrets.json");

        Assert.That(Convert.FromBase64String(resolvedKey), Has.Length.GreaterThanOrEqualTo(32));
    }

    [Test]
    public void ResolveRedactionKeyMaterial_WhenArbitraryPassphraseIsConfigured_DerivesStableValidBase64Key()
    {
        string firstResolvedKey = RegisterDependentServices.ResolveRedactionKeyMaterial("redaction-key");
        string secondResolvedKey = RegisterDependentServices.ResolveRedactionKeyMaterial("redaction-key");

        Assert.Multiple(() =>
        {
            Assert.That(firstResolvedKey, Is.EqualTo(secondResolvedKey));
            Assert.That(Convert.FromBase64String(firstResolvedKey), Has.Length.GreaterThanOrEqualTo(32));
        });
    }

    [Test]
    public void ResolveRedactionKeyMaterial_WhenValidBase64KeyIsConfigured_PreservesOriginalValue()
    {
        string configuredKey = Convert.ToBase64String(new byte[32]);

        string resolvedKey = RegisterDependentServices.ResolveRedactionKeyMaterial(configuredKey);

        Assert.That(resolvedKey, Is.EqualTo(configuredKey));
    }

    private static AppSettings CreateValidAppSettings()
    {
        return new AppSettings
        {
            AllowedHosts = "*",
            Auth = new Auth
            {
                LoginTimeInMinutes = 20
            },
            HttpClients = new HttpClients
            {
                Resilience = new Resilience
                {
                    TimeOutInSeconds = 30
                }
            },
            ImpersonatingCookie = "acting-as",
            Logging = new Logging
            {
                LogLevel = new LoggingLevels
                {
                    Default = nameof(LogLevel.Information),
                    Microsoft = nameof(LogLevel.Warning),
                    System = nameof(LogLevel.Warning)
                }
            },
            RedactionKey = "redaction-key"
        };
    }
}
