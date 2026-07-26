using Microsoft.Extensions.Options;
using SystemUptimeTracker.Common.Constants;
using SystemUptimeTracker.Common.Helpers.Extensions;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using SystemUptimeTracker.Api.Models.Ui.InfoPage;
using SystemUptimeTracker.Api.Services.Info.Interface;
using System.Globalization;
using System.Reflection;
using System.Xml.Serialization;

namespace SystemUptimeTracker.Api.Services.Info;

public class InfoService : IInfoService
{
    private readonly InfoPageDetails _canaryPageDetails = new();
    private readonly ILogger<InfoService> _logger;

    // needed for serialization
    /// <summary>
    ///     Initializes a new instance of the <see cref="InfoService" /> class.
    /// </summary>
    public InfoService(
        ILogger<InfoService> logger,
        IOptions<AppSettings> appSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Serializes to response.
    /// </summary>
    /// <returns></returns>
    public string SerializeToResponseXml()
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(SerializeToResponseXml));
        }

        PopulateProjectInfoCollection();
        _logger.LogInformation("Serializing status information as XML.");

        XmlSerializerNamespaces serializerNamespaces = new XmlSerializerNamespaces();
        serializerNamespaces.Add("", "");

        XmlSerializer serializer = new XmlSerializer(_canaryPageDetails.GetType());
        using StringWriter textWriter = new StringWriter();

        serializer.Serialize(textWriter, _canaryPageDetails, serializerNamespaces);
        string response = textWriter.ToString();

        return response;
    }

    public string? SerializeToResponseJson()
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(LoggingTemplates.DEBUG_METHOD_ENTRY_MESSAGE, GetType().Name, nameof(SerializeToResponseJson));
        }

        PopulateProjectInfoCollection();
        _logger.LogInformation("Serializing status information as JSON.");

        return _canaryPageDetails.ToJson();
    }

    private void PopulateProjectInfoCollection()
    {
        Assembly assembly = GetType().Assembly;
        _canaryPageDetails.Title = "System Uptime Tracker API";

        Version? assemblyVersion = assembly.GetName().Version;
        _canaryPageDetails.ProjectInfoCollection = new List<ProjectInfoDetails>
        {
            new("Current Time on Server", DateTime.Now.ToString(CultureInfo.CurrentCulture)),
            new("Product Name", assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product!),
            new("Product Version", $"{assemblyVersion?.Major}.{assemblyVersion?.Minor}.{assemblyVersion?.Build}"),
            new("Build Date", new FileInfo(assembly.Location ?? "").LastWriteTime.ToString(CultureInfo.CurrentCulture)),
            new("Build Version", assemblyVersion?.ToString()!),
            new("Runtime .NET Framework Version", Environment.Version.ToString()),
            new("Product .NET Framework Version", assembly.ImageRuntimeVersion),
            new("Server OS Version", Environment.OSVersion.VersionString),
            new("Single Resource", ""),
            new("App Relative Path", "~/canary")
        };
    }
}
