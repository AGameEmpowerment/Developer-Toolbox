using FluentValidation;
using SystemUptimeTracker.Api.Models.ApplicationSettings;

namespace SystemUptimeTracker.Api.Helpers.Validators;

public class AppSettingsOptionsValidator : AbstractValidator<AppSettings>
{
    public AppSettingsOptionsValidator()
    {
        RuleFor(x => x.Logging.LogLevel.Default)
            .IsEnumName(typeof(LogLevel));
        RuleFor(x => x.Logging.LogLevel.Microsoft)
            .IsEnumName(typeof(LogLevel));
        RuleFor(x => x.Logging.LogLevel.System)
            .IsEnumName(typeof(LogLevel));

        RuleFor(x => x.RedactionKey)
            .NotEmpty();

        //RuleFor(x => x.KeyVaultUri)
        //    .NotEmpty();

        RuleFor(x => x.FeatureManagement.OpenApiEnabled)
            .Must(_ => true);

        RuleFor(x => x.AllowedHosts)
            .NotEmpty();

        When(x => x.Auth.Jwt.Enabled, () =>
        {
            RuleFor(x => x.Auth.Jwt.Issuer)
                .NotEmpty();
            RuleFor(x => x.Auth.Jwt.Audience)
                .NotEmpty();
            RuleFor(x => x.Auth.Jwt.SigningKey)
                .MinimumLength(32)
                .Must(value => !value.StartsWith("__SET_", StringComparison.OrdinalIgnoreCase))
                .WithMessage("Auth:Jwt:SigningKey must be supplied by user secrets or environment configuration.");
            RuleFor(x => x.Auth.Jwt.ClockSkewSeconds)
                .InclusiveBetween(0, 300);
        });
        RuleFor(x => x.Auth.LoginTimeInMinutes)
            .InclusiveBetween(1, 120)
            .WithMessage("The LoginTimeInMinutes duration in minutes can be between 1 and 120 minutes.");

        RuleFor(x => x.HttpClients.Resilience.TimeOutInSeconds)
            .InclusiveBetween(1, 120)
            .WithMessage("The TimeOutInSeconds duration in seconds can be between 1 and 120 minutes.");

        When(ShouldUseRedis, () =>
        {
            RuleFor(x => x.Redis.InstanceName)
                .NotEmpty();
            RuleFor(x => x.Redis.Url)
                .NotEmpty();
        });

        RuleFor(x => x.Auth.LoginTimeInMinutes)
            .InclusiveBetween(10, 60)
            .WithMessage("The login time window is 10 and 60 minutes.");

        RuleFor(x => x.ImpersonatingCookie)
            .NotEmpty()
            .Must(x => x.ToLower().Contains("acting-as"));
    }

    private static bool ShouldUseRedis(AppSettings appSettings)
    {
        return appSettings.Redis is not null &&
               !appSettings.Redis.LocalOverride &&
               !string.IsNullOrWhiteSpace(appSettings.Redis.Url);
    }
}
