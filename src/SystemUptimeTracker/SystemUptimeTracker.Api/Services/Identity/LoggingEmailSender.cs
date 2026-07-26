using Microsoft.AspNetCore.Identity;
using SystemUptimeTracker.Data.Identity;

namespace SystemUptimeTracker.Api.Services.Identity;

public sealed class LoggingEmailSender : IEmailSender<ApplicationUser>
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        _logger.LogWarning(
            "Identity confirmation email for {Email} was not sent because no outbound email provider is configured.",
            email);

        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        _logger.LogWarning(
            "Identity password reset code email for {Email} was not sent because no outbound email provider is configured.",
            email);

        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        _logger.LogWarning(
            "Identity password reset link email for {Email} was not sent because no outbound email provider is configured.",
            email);

        return Task.CompletedTask;
    }
}