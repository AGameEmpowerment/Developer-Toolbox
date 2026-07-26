using System.Diagnostics.CodeAnalysis;

namespace SystemUptimeTracker.Common.Constants;

/// <summary>
/// Contains logging templates used throughout the project.
/// </summary>
[ExcludeFromCodeCoverage]
public class LoggingTemplates
{
    #region Base Templates

    /// <summary>
    /// Template for logging method entry debug messages.
    /// </summary>
    public const string DEBUG_METHOD_ENTRY_MESSAGE = "'{Class}.{Method}' called";

    #endregion

    #region ClientWrapper

    /// <summary>
    /// Template for logging standard HTTP resource request messages.
    /// </summary>
    public const string INFO_HTTP_RESOURCE_STANDARD_MESSAGE = "Request Resource Path:'{resourcePath}'";

    #endregion
}