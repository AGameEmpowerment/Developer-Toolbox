using System.Diagnostics.CodeAnalysis;

namespace SystemUptimeTracker.Api.Constants;

[ExcludeFromCodeCoverage]
public class LoggingTemplates : Common.Constants.LoggingTemplates
{
    #region ClientWrapper

    public new const string INFO_HTTP_RESOURCE_STANDARD_MESSAGE = "Request Resource Path:'{resourcePath}'";

    #endregion
}
