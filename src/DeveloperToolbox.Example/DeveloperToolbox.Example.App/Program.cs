using DeveloperToolbox.Example.App;
using DeveloperToolbox.Example.Data;
using DeveloperToolbox.Example.Messaging;
using DeveloperToolbox.Example.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

IHost? host = null;
using var stop = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, message) => { message.Cancel = true; stop.Cancel(); };
Console.CancelKeyPress += cancel;
try
{
    if (args.Length == 0 || !string.Equals(args[0], "validate", StringComparison.OrdinalIgnoreCase))
    {
        stop.CancelAfter(TimeSpan.FromSeconds(60));
    }
    Environment.ExitCode = await SampleApplication.RunAsync(args, CreateRunner,
        Console.Out, Console.Error, stop.Token);
}
finally
{
    Console.CancelKeyPress -= cancel;
    host?.Dispose();
}

// Help and invalid commands never initialize a host or unrelated resource clients.
SampleRunner CreateRunner()
{
    if (host is not null)
    {
        return ResolveRunner();
    }
    var builder = Host.CreateApplicationBuilder();
    if (string.Equals(args[0], "validate", StringComparison.OrdinalIgnoreCase))
    {
        // Validation owns its output so SDK diagnostics cannot corrupt JSON or expose settings.
        builder.Logging.ClearProviders();
    }
    var dotenvPath = Environment.GetEnvironmentVariable("TOOLBOX_ENV_FILE");
    if (!string.IsNullOrWhiteSpace(dotenvPath))
    {
        builder.Configuration.AddInMemoryCollection(ToolboxEnvironment.Read(dotenvPath));
    }
    builder.Configuration.AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true)
        .AddEnvironmentVariables();
    builder.Services.AddHttpClient();
    builder.Services.AddHttpClient(ResourceSamples.HttpClientName)
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    builder.Services.AddToolboxDatabases(builder.Configuration);
    builder.Services.AddSingleton<IResourceSamples, ResourceSamples>();
    builder.Services.AddSingleton<IMessagingSamples, MessagingSamples>();
    builder.Services.AddSingleton<ServiceBusValidationSamples>();
    host = builder.Build();
    return ResolveRunner();
}

SampleRunner ResolveRunner() => new(host!.Services.GetRequiredService<IResourceSamples>(),
    host.Services.GetRequiredService<IMessagingSamples>(), host.Services.GetRequiredService<IDatabaseSamples>(),
    host.Services.GetRequiredService<ServiceBusValidationSamples>());
