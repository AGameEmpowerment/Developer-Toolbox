using SystemUptimeTracker.Api.Models.ApplicationSettings;

namespace SystemUptimeTracker.Api.Helpers.Extensions;

/// <summary>
/// Logging extension methods.
/// Add new methods for data that should be logged and has sensitive data contained within.
/// </summary>
public static partial class Logging
{
#pragma warning disable EXTEXP0003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    [LoggerMessage(LogLevel.Information, "Application Settings")]
    public static partial void LogAppSettings(this ILogger logger, [LogProperties(Transitive = true)] AppSettings settings);
#pragma warning restore EXTEXP0003 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
}