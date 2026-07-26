using System.Collections.Generic;
using Microsoft.Extensions.Options;
using SystemUptimeTracker.Qa.Automation.Support;

namespace SystemUptimeTracker.Qa.Automation.Configuration;

public sealed class SystemUptimeTrackerPageCatalog : ISystemUptimeTrackerPageCatalog
{
    private readonly SystemUptimeTrackerWebValidationOptions _options;
    private readonly QaAutomationExecutionOptions _qaAutomationExecution;
    private readonly IReadOnlyDictionary<string, string> _pages;
    private readonly IReadOnlyDictionary<string, string> _titles;

    public SystemUptimeTrackerPageCatalog(
        IOptions<SystemUptimeTrackerWebValidationOptions> options,
        IOptions<QaAutomationExecutionOptions> qaAutomationExecution)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _qaAutomationExecution = qaAutomationExecution?.Value ?? throw new ArgumentNullException(nameof(qaAutomationExecution));
        _pages = new Dictionary<string, string>(_options.Pages ?? [], StringComparer.OrdinalIgnoreCase);
        _titles = new Dictionary<string, string>(_options.Titles ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public bool UseInternalPages => _options.UseInternalPages;

    public string GetPageUrl(string pageKey, string fallbackUrl)
    {
        string? resolvedBaseUrl = _qaAutomationExecution.UseExternalHost
            ? _qaAutomationExecution.WebBaseUrl
            : null;

        if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
        {
            resolvedBaseUrl = _options.BaseUrl;
        }

        _pages.TryGetValue(pageKey, out string? path);

        if (string.IsNullOrWhiteSpace(path))
        {
            if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
            {
                return fallbackUrl;
            }

            if (IsHomePageKey(pageKey))
            {
                return resolvedBaseUrl.TrimEnd('/');
            }

            string? derivedPath = TryDerivePathFromFallbackUrl(fallbackUrl);
            return string.IsNullOrWhiteSpace(derivedPath)
                ? fallbackUrl
                : $"{resolvedBaseUrl.TrimEnd('/')}{NormalizePath(derivedPath)}";
        }

        if (string.IsNullOrWhiteSpace(resolvedBaseUrl))
        {
            return fallbackUrl;
        }

        return $"{resolvedBaseUrl.TrimEnd('/')}{NormalizePath(path)}";
    }

    public string GetPageTitle(string pageKey, string fallbackTitle)
    {
        return _titles.TryGetValue(pageKey, out string? title) && !string.IsNullOrWhiteSpace(title)
            ? title
            : fallbackTitle;
    }

    private static bool IsHomePageKey(string pageKey)
    {
        return string.Equals(pageKey, "Home", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        return path.StartsWith('/') ? path : $"/{path}";
    }

    private static string? TryDerivePathFromFallbackUrl(string fallbackUrl)
    {
        if (string.IsNullOrWhiteSpace(fallbackUrl))
        {
            return null;
        }

        if (Uri.TryCreate(fallbackUrl, UriKind.Absolute, out Uri? absoluteUri))
        {
            return absoluteUri.PathAndQuery;
        }

        return Uri.TryCreate(fallbackUrl, UriKind.Relative, out Uri? relativeUri)
            ? relativeUri.OriginalString
            : null;
    }
}
