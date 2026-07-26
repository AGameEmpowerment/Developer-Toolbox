using SystemUptimeTracker.Api.Models.ApplicationSettings;

namespace SystemUptimeTracker.Api.Helpers.Interfaces;

/// <summary>
/// Provides a single place to expose dependencies shared by controllers.
/// Add shared services or settings here so controllers (or a base controller) can consume them
/// without requiring changes to many constructor signatures.
/// Note: `ILogger&lt;T&gt;` is typically specific to each controller type and is therefore not included in this bundle.
/// </summary>
public interface IControllerDependencyBundle
{
    AppSettings AppSettings { get; }
}
