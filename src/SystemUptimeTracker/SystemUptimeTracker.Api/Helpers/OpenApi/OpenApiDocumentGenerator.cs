using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.OpenApi;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SystemUptimeTracker.Api.Helpers.OpenApi;

/// <summary>
/// Generates the runtime OpenAPI document without coupling the application to an ASP.NET OpenAPI source generator.
/// </summary>
public sealed class OpenApiDocumentGenerator(IApiDescriptionGroupCollectionProvider apiDescriptions)
{
    private const string DOCUMENT_NAME = "v1";
    private const string SECURITY_SCHEME_NAME = "Bearer";
    private const int MAX_SCHEMA_DEPTH = 4;

    public OpenApiDocument Generate(string documentName)
    {
        if (!string.Equals(documentName, DOCUMENT_NAME, StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyNotFoundException($"OpenAPI document '{documentName}' is not registered.");
        }

        var document = new OpenApiDocument
        {
            Info = CreateInfo(),
            Paths = new OpenApiPaths(),
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    [SECURITY_SCHEME_NAME] = new OpenApiSecurityScheme
                    {
                        Scheme = "Bearer",
                        BearerFormat = "JWT",
                        In = ParameterLocation.Header,
                        Name = "Authorization",
                        Description = "Bearer authentication with a JWT access token.",
                        Type = SecuritySchemeType.Http
                    }
                }
            }
        };

        var securityScheme = new OpenApiSecuritySchemeReference(SECURITY_SCHEME_NAME, document);
        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [securityScheme] = []
            }
        ];

        foreach (ApiDescription description in apiDescriptions.ApiDescriptionGroups.Items
                     .SelectMany(group => group.Items)
                     .Where(item => !string.IsNullOrWhiteSpace(item.HttpMethod) && !string.IsNullOrWhiteSpace(item.RelativePath)))
        {
            AddOperation(document, description);
        }

        return document;
    }

    private static OpenApiInfo CreateInfo()
    {
        Version? assemblyVersion = typeof(OpenApiDocumentGenerator).Assembly.GetName().Version;

        return new OpenApiInfo
        {
            Title = "System Uptime Tracker",
            Version = assemblyVersion?.ToString() ?? "1.0.0",
            Description = $"API documentation for System Uptime Tracker. © {DateTime.UtcNow:yyyy} - Build Version: {assemblyVersion}",
            TermsOfService = new Uri("https://systemuptimetracker.example/legal/terms-of-use"),
            Contact = new OpenApiContact
            {
                Name = "Solution Manager",
                Email = "support@systemuptimetracker.example",
                Url = new Uri("https://systemuptimetracker.example")
            },
            License = new OpenApiLicense
            {
                Name = "Internal Only",
                Url = new Uri("https://systemuptimetracker.example")
            }
        };
    }

    private static void AddOperation(OpenApiDocument document, ApiDescription description)
    {
        string path = NormalizePath(description.RelativePath!);
        HttpMethod method = new(description.HttpMethod!);

        if (!document.Paths!.TryGetValue(path, out IOpenApiPathItem? existingPath))
        {
            existingPath = new OpenApiPathItem
            {
                Operations = []
            };
            document.Paths[path] = existingPath;
        }

        if (existingPath is not OpenApiPathItem pathItem)
        {
            return;
        }

        pathItem.Operations ??= [];
        if (pathItem.Operations.ContainsKey(method))
        {
            return;
        }

        var operation = new OpenApiOperation
        {
            OperationId = CreateOperationId(description, method, path),
            Parameters = CreateParameters(description),
            Responses = CreateResponses(description)
        };

        operation.RequestBody = CreateRequestBody(description);
        DefaultResponsesOpenApiCustomizer.Apply(operation, description);
        pathItem.Operations[method] = operation;
    }

    private static string NormalizePath(string relativePath)
    {
        string path = relativePath.Split('?', 2)[0];
        string normalized = System.Text.RegularExpressions.Regex.Replace(path, @"\{([^}:]+)(?::[^}]+)?\}", "{$1}");
        return normalized.StartsWith('/') ? normalized : $"/{normalized}";
    }

    private static string CreateOperationId(ApiDescription description, HttpMethod method, string path)
    {
        description.ActionDescriptor.RouteValues.TryGetValue("controller", out string? controller);
        description.ActionDescriptor.RouteValues.TryGetValue("action", out string? action);
        string candidate = string.Join('_', new[] { controller, action, method.Method }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = $"{method.Method}_{path}";
        }

        return System.Text.RegularExpressions.Regex.Replace(candidate, "[^A-Za-z0-9_]", "_");
    }

    private static IList<IOpenApiParameter> CreateParameters(ApiDescription description)
    {
        var parameters = new List<IOpenApiParameter>();

        foreach (ApiParameterDescription parameter in description.ParameterDescriptions.Where(IsOpenApiParameter))
        {
            ParameterLocation location = parameter.Source == BindingSource.Path
                ? ParameterLocation.Path
                : parameter.Source == BindingSource.Header
                    ? ParameterLocation.Header
                    : ParameterLocation.Query;

            parameters.Add(new OpenApiParameter
            {
                Name = parameter.Name,
                In = location,
                Required = location == ParameterLocation.Path || parameter.IsRequired,
                Schema = CreateSchema(parameter.Type ?? parameter.ModelMetadata?.ModelType, 0, [])
            });
        }

        return parameters;
    }

    private static bool IsOpenApiParameter(ApiParameterDescription parameter)
    {
        return parameter.Source == BindingSource.Path ||
               parameter.Source == BindingSource.Query ||
               parameter.Source == BindingSource.Header;
    }

    private static IOpenApiRequestBody? CreateRequestBody(ApiDescription description)
    {
        ApiParameterDescription? body = description.ParameterDescriptions.FirstOrDefault(parameter =>
            parameter.Source == BindingSource.Body ||
            parameter.Source == BindingSource.Form ||
            parameter.Source == BindingSource.FormFile);

        if (body is null)
        {
            return null;
        }

        string[] contentTypes = description.SupportedRequestFormats
            .Select(format => format.MediaType)
            .Where(mediaType => !string.IsNullOrWhiteSpace(mediaType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;

        if (contentTypes.Length == 0)
        {
            contentTypes = ["application/json"];
        }

        IOpenApiSchema schema = CreateSchema(body.Type ?? body.ModelMetadata?.ModelType, 0, []);
        return new OpenApiRequestBody
        {
            Required = body.IsRequired,
            Content = contentTypes.ToDictionary(
                contentType => contentType,
                _ => (IOpenApiMediaType)new OpenApiMediaType { Schema = schema },
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static OpenApiResponses CreateResponses(ApiDescription description)
    {
        var responses = new OpenApiResponses();

        foreach (ApiResponseType responseType in description.SupportedResponseTypes)
        {
            string statusCode = responseType.StatusCode.ToString();
            var response = new OpenApiResponse
            {
                Description = GetResponseDescription(responseType.StatusCode)
            };

            if (responseType.Type is not null && responseType.Type != typeof(void))
            {
                string[] contentTypes = responseType.ApiResponseFormats
                    .Select(format => format.MediaType)
                    .Where(mediaType => !string.IsNullOrWhiteSpace(mediaType))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()!;

                if (contentTypes.Length == 0)
                {
                    contentTypes = ["application/json"];
                }

                IOpenApiSchema schema = CreateSchema(responseType.Type, 0, []);
                response.Content = contentTypes.ToDictionary(
                    contentType => contentType,
                    _ => (IOpenApiMediaType)new OpenApiMediaType { Schema = schema },
                    StringComparer.OrdinalIgnoreCase);
            }

            responses[statusCode] = response;
        }

        if (responses.Count == 0)
        {
            responses[StatusCodes.Status200OK.ToString()] = new OpenApiResponse { Description = "OK" };
        }

        return responses;
    }

    private static string GetResponseDescription(int statusCode)
    {
        return Enum.IsDefined(typeof(System.Net.HttpStatusCode), statusCode)
            ? ((System.Net.HttpStatusCode)statusCode).ToString()
            : $"HTTP {statusCode}";
    }

    private static IOpenApiSchema CreateSchema(Type? declaredType, int depth, HashSet<Type> visited)
    {
        Type type = UnwrapType(declaredType ?? typeof(object));
        bool nullable = Nullable.GetUnderlyingType(type) is not null;
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string) || type == typeof(char))
        {
            return Primitive(JsonSchemaType.String, nullable: nullable);
        }

        if (type == typeof(bool))
        {
            return Primitive(JsonSchemaType.Boolean, nullable: nullable);
        }

        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
            type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong))
        {
            return Primitive(JsonSchemaType.Integer, type == typeof(long) || type == typeof(ulong) ? "int64" : "int32", nullable);
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return Primitive(JsonSchemaType.Number, type == typeof(float) ? "float" : "double", nullable);
        }

        if (type == typeof(Guid))
        {
            return Primitive(JsonSchemaType.String, "uuid", nullable);
        }

        if (type == typeof(DateOnly))
        {
            return Primitive(JsonSchemaType.String, "date", nullable);
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return Primitive(JsonSchemaType.String, "date-time", nullable);
        }

        if (type == typeof(TimeOnly) || type == typeof(TimeSpan))
        {
            return Primitive(JsonSchemaType.String, "time", nullable);
        }

        if (type == typeof(byte[]) || typeof(IFormFile).IsAssignableFrom(type))
        {
            return Primitive(JsonSchemaType.String, "binary", nullable);
        }

        if (type.IsEnum)
        {
            return new OpenApiSchema
            {
                Type = AddNull(JsonSchemaType.String, nullable),
                Enum = Enum.GetNames(type).Select(name => (JsonNode)JsonValue.Create(name)!).ToList()
            };
        }

        Type? elementType = GetEnumerableElementType(type);
        if (elementType is not null)
        {
            return new OpenApiSchema
            {
                Type = AddNull(JsonSchemaType.Array, nullable),
                Items = CreateSchema(elementType, depth + 1, visited)
            };
        }

        if (depth >= MAX_SCHEMA_DEPTH || !visited.Add(type))
        {
            return Primitive(JsonSchemaType.Object, nullable: nullable);
        }

        var properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
                     .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>() is null))
        {
            string name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            properties[name] = CreateSchema(property.PropertyType, depth + 1, visited);
        }

        visited.Remove(type);
        return new OpenApiSchema
        {
            Type = AddNull(JsonSchemaType.Object, nullable),
            Properties = properties,
            AdditionalPropertiesAllowed = false
        };
    }

    private static Type UnwrapType(Type type)
    {
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            return type.GetGenericArguments()[0];
        }

        return type;
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type == typeof(string) || type == typeof(byte[]))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static OpenApiSchema Primitive(JsonSchemaType type, string? format = null, bool nullable = false)
    {
        return new OpenApiSchema
        {
            Type = AddNull(type, nullable),
            Format = format
        };
    }

    private static JsonSchemaType AddNull(JsonSchemaType type, bool nullable)
    {
        return nullable ? type | JsonSchemaType.Null : type;
    }
}
