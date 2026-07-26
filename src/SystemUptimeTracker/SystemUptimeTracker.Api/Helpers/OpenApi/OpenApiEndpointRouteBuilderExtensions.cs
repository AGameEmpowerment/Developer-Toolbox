using Microsoft.OpenApi;

namespace SystemUptimeTracker.Api.Helpers.OpenApi;

public static class OpenApiEndpointRouteBuilderExtensions
{
    private const string DOCUMENT_NAME = "v1";
    private const string ROUTE_PATTERN = "/openapi/{documentName}.json";

    public static IEndpointConventionBuilder MapOpenApiDocument(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet(
                ROUTE_PATTERN,
                async (string documentName, HttpContext context, OpenApiDocumentGenerator generator, CancellationToken cancellationToken) =>
                {
                    if (!string.Equals(documentName, DOCUMENT_NAME, StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.NotFound();
                    }

                    OpenApiDocument document = generator.Generate(documentName);
                    context.Response.ContentType = "application/vnd.oai.openapi+json;version=3.1";
                    string payload = await document.SerializeAsJsonAsync(
                        OpenApiSpecVersion.OpenApi3_1,
                        cancellationToken);
                    await context.Response.WriteAsync(payload, cancellationToken);
                    return Results.Empty;
                })
            .ExcludeFromDescription()
            .AllowAnonymous();
    }
}
