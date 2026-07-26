using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Text.Json;
using SystemUptimeTracker.Api.Helpers.OpenApi;

namespace SystemUptimeTracker.Tests.Helpers.OpenApi;

[TestFixture(Category = "Integration")]
public sealed class OpenApiDocumentIntegrationTests
{
    [Test]
    public async Task OpenApiEndpoint_ReturnsSerializedDocumentForDiscoveredEndpoints()
    {
        await using WebApplication app = await CreateApplicationAsync();

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/openapi/v1.json");
        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/vnd.oai.openapi+json"));
            Assert.That(document.RootElement.GetProperty("openapi").GetString(), Does.StartWith("3.1"));
            Assert.That(document.RootElement.GetProperty("info").GetProperty("title").GetString(), Is.EqualTo("System Uptime Tracker"));
            Assert.That(document.RootElement.GetProperty("paths").TryGetProperty("/test/widgets/{id}", out _), Is.True);
            Assert.That(document.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _), Is.True);
        });
    }

    [Test]
    public async Task OpenApiEndpoint_ForUnknownDocument_ReturnsNotFound()
    {
        await using WebApplication app = await CreateApplicationAsync();

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/openapi/unknown.json");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private static async Task<WebApplication> CreateApplicationAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSingleton<OpenApiDocumentGenerator>();

        WebApplication app = builder.Build();
        app.MapGet(
            "/test/widgets/{id:int}",
            (int id, string? filter) => TypedResults.Ok(new WidgetResponse(id, filter)));
        app.MapOpenApiDocument();
        await app.StartAsync();
        return app;
    }

    private sealed record WidgetResponse(int Id, string? Filter);
}
