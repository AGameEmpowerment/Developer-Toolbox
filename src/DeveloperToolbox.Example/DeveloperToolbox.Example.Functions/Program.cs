using DeveloperToolbox.Example.Functions;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

// This host is a local developer example. Credentials belong in ignored local.settings.json.
var builder = FunctionsApplication.CreateBuilder(args);
builder.Services.AddToolboxFunctionSamples(builder.Configuration);
await builder.Build().RunAsync();
