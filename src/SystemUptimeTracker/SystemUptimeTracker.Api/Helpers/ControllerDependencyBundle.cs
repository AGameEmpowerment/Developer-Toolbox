using Microsoft.Extensions.Options;
using SystemUptimeTracker.Api.Helpers.Interfaces;
using SystemUptimeTracker.Api.Models.ApplicationSettings;

namespace SystemUptimeTracker.Api.Helpers;

public class ControllerDependencyBundle : IControllerDependencyBundle
{
    private readonly IOptions<AppSettings> _appSettings;

    // ReSharper disable once ConvertToPrimaryConstructor
    public ControllerDependencyBundle(
        IOptions<AppSettings> options)
    {
        _appSettings = options;
    }

    public AppSettings AppSettings => _appSettings.Value;
}
