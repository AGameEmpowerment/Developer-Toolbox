using Asp.Versioning;
using Azure.Identity;
using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.FeatureManagement;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SystemUptimeTracker.Common.Helpers.Data;
using SystemUptimeTracker.Common.Helpers.Filter;
using SystemUptimeTracker.Data.Identity;
using SystemUptimeTracker.Api.Authorization;
using SystemUptimeTracker.Api.Connection.DependencyInjection;
using SystemUptimeTracker.Api.Constants;
using SystemUptimeTracker.Api.Helpers.Web;
using SystemUptimeTracker.Api.Helpers.Caching;
using SystemUptimeTracker.Api.Services.Identity;
using SystemUptimeTracker.Api.Factories.DependencyInjection;
using SystemUptimeTracker.Api.Helpers.DependencyInjection;
using SystemUptimeTracker.Api.Helpers.Extensions;
using SystemUptimeTracker.Api.Helpers.Handlers;
using SystemUptimeTracker.Api.Helpers.Health;
using SystemUptimeTracker.Api.Helpers.OpenApi;
using SystemUptimeTracker.Api.Helpers.Tracing;
using SystemUptimeTracker.Api.Models.ApplicationSettings;
using SystemUptimeTracker.Api.Models.Ui.Permissions;
using SystemUptimeTracker.Api.Repositories.DependencyInjection;
using SystemUptimeTracker.Api.Services.DependencyInjection;
using SystemUptimeTracker.ServiceDefaults;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace SystemUptimeTracker.Api;

public static class RegisterDependentServices
{
    private static readonly string _swaggerName = "SystemUptimeTracker";
    private static readonly ActivitySource _activitySource = new($"{_swaggerName} API");

    public static bool HasConfiguredApplicationInsights(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return !connectionString.Contains("Replace-Key", StringComparison.OrdinalIgnoreCase) &&
               !connectionString.Equals("na", StringComparison.OrdinalIgnoreCase) &&
               !connectionString.Equals("n/a", StringComparison.OrdinalIgnoreCase) &&
               !connectionString.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    public static WebApplicationBuilder RegisterServices(this WebApplicationBuilder builder, string[] args, out AppSettings appSettings)
    {
        // Local variable to avoid CS1628 Compiler Error.
        AppSettings settings = appSettings = RegisterConfiguration(builder, args);

        if (settings.FeatureManagement.AspireEnabled)
        {
            builder.AddSystemUptimeTrackerServiceDefaults(enableOpenTelemetry: false);
        }

        SetupLoggingAndTelemetry(builder, appSettings);

        builder.Services.AddControllersWithViews().AddNewtonsoftJson(options =>
        {
            options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            options.SerializerSettings.Formatting = Formatting.Indented;
            options.SerializerSettings.Converters.Add(new StringEnumConverter());
            options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        });
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("FrontendCors", policy =>
            {
                if (settings.Cors.AllowedOrigins.Length == 0)
                {
                    return;
                }

                policy
                    .WithOrigins(settings.Cors.AllowedOrigins)
                    .WithExposedHeaders(RequestTraceContext.TRACE_ID_HEADER_NAME)
                    .AllowCredentials()
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        builder.Services.AddRequestDecompression();
        builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; });
        ConfigureForwardedHeaders(builder, settings);

        // Provide safe defaults if configuration sections are missing
        var timeoutSeconds = appSettings.HttpClients?.Resilience?.TimeOutInSeconds ?? 30;

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler(o =>
            {
                o.TotalRequestTimeout = new HttpTimeoutStrategyOptions()
                {
                    Name = "TotalTimeout",
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds)
                };
                o.AttemptTimeout = new HttpTimeoutStrategyOptions()
                {
                    Name = "TotalTimeout",
                    Timeout = TimeSpan.FromSeconds(timeoutSeconds),
                };
                o.CircuitBreaker = new HttpCircuitBreakerStrategyOptions()
                {
                    Name = "TotalTimeout",
                    BreakDuration = TimeSpan.FromSeconds(timeoutSeconds),
                    SamplingDuration = TimeSpan.FromSeconds(timeoutSeconds * 2)
                };
            });

            if (settings.FeatureManagement.AspireEnabled)
            {
                http.AddServiceDiscovery();
            }
        });

        builder.Services.AddApiVersioning(c =>
        {
            c.DefaultApiVersion = new ApiVersion(1, 0);
            c.AssumeDefaultVersionWhenUnspecified = true;
            c.ReportApiVersions = true;
        });

        if (settings.FeatureManagement.OpenApiEnabled)
        {
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSingleton<OpenApiDocumentGenerator>();
        }

        builder.Services.AddFeatureManagement();
        builder.Services.TryAddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationMiddlewareResultHandler>();

        ConfigureDistributedCaching(builder, settings);
        ConfigureDataProtection(builder, settings);
        builder.SetDependencyInjection(settings);

        RepositoriesResolver.RegisterIdentityData(builder);

        builder.Services.AddDatabaseDeveloperPageExceptionFilter();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = SystemUptimeTrackerAuthenticationSchemes.ANTIFORGERY_HEADER_NAME;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        AuthenticationBuilder authenticationBuilder = builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
                options.DefaultChallengeScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
                options.DefaultScheme = SystemUptimeTrackerAuthenticationSchemes.APPLICATION;
            })
            .AddPolicyScheme(SystemUptimeTrackerAuthenticationSchemes.APPLICATION, "SystemUptimeTracker application authentication", options =>
            {
                options.ForwardDefaultSelector = context =>
                    SystemUptimeTrackerAuthenticationSchemeSelector.Resolve(context, settings.Auth.Jwt.Enabled);
            });

        authenticationBuilder.AddIdentityCookies();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
        });
        authenticationBuilder.AddBearerToken(IdentityConstants.BearerScheme);

        if (settings.Auth.Jwt.Enabled)
        {
            authenticationBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Auth.Jwt.SigningKey)),
                    ValidateIssuer = true,
                    ValidIssuer = settings.Auth.Jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Auth.Jwt.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(settings.Auth.Jwt.ClockSkewSeconds),
                    NameClaimType = "sub",
                    RoleClaimType = "roles"
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateLocalJwtAccountAsync
                };
            });
        }

        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.Zero;
        });

        ConfigureAuthorization(builder);
        builder.Services.AddTransient<IClaimsTransformation, IdentityStoreClaimsTransformation>();

        // Health Checks
        // See: https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks for additional prebuilt checks
        var healthChecks = builder.Services.AddHealthChecks()
            .AddSqlServer(appSettings.ConnectionStrings.DefaultConnection);

        if (ShouldUseRedisCaching(settings))
        {
            healthChecks.AddCheck<RedisCacheHealthCheck>(nameof(RedisCacheHealthCheck));
        }

        return builder;
    }

    private static void ConfigureDataProtection(WebApplicationBuilder builder, AppSettings appSettings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(appSettings);

        DataProtectionSettings settings = appSettings.DataProtection ?? new DataProtectionSettings();
        string applicationName = string.IsNullOrWhiteSpace(settings.ApplicationName)
            ? "SystemUptimeTracker"
            : settings.ApplicationName.Trim();
        string keyRingPath = ResolveDataProtectionKeyRingPath(builder, settings, applicationName);

        Directory.CreateDirectory(keyRingPath);

        IDataProtectionBuilder dataProtectionBuilder = builder.Services
            .AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

        if (OperatingSystem.IsWindows() && settings.ProtectKeysWithDpapi)
        {
            dataProtectionBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }
    }

    private static string ResolveDataProtectionKeyRingPath(
        WebApplicationBuilder builder,
        DataProtectionSettings settings,
        string applicationName)
    {
        if (!string.IsNullOrWhiteSpace(settings.KeyRingPath))
        {
            return Path.GetFullPath(settings.KeyRingPath.Trim());
        }

        string[] candidateRoots = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")
            ?
            [
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                builder.Environment.ContentRootPath
            ]
            :
            [
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                builder.Environment.ContentRootPath
            ];

        foreach (string candidateRoot in candidateRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            string candidatePath = Path.Combine(candidateRoot, applicationName, "DataProtection-Keys");
            if (CanCreateOrWriteDirectory(candidatePath))
            {
                return candidatePath;
            }
        }

        return Path.Combine(builder.Environment.ContentRootPath, ".app-data", applicationName, "DataProtection-Keys");
    }

    private static bool CanCreateOrWriteDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            string probePath = Path.Combine(path, $".dp-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "probe");
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ConfigureForwardedHeaders(WebApplicationBuilder builder, AppSettings appSettings)
    {
        string[] knownProxies = appSettings.ForwardedHeaders.KnownProxies;
        string[] knownIpNetworks = appSettings.ForwardedHeaders.KnownIpNetworks;

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
                                       Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;

            foreach (string knownProxy in knownProxies.Where(proxy => !string.IsNullOrWhiteSpace(proxy)))
            {
                if (!IPAddress.TryParse(knownProxy, out IPAddress? parsedProxy))
                {
                    throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains an invalid IP address: {knownProxy}");
                }

                options.KnownProxies.Add(parsedProxy);
            }

            foreach (string knownIpNetwork in knownIpNetworks.Where(network => !string.IsNullOrWhiteSpace(network)))
            {
                if (!System.Net.IPNetwork.TryParse(
                    knownIpNetwork,
                    out System.Net.IPNetwork parsedNetwork))
                {
                    throw new InvalidOperationException($"ForwardedHeaders:KnownIPNetworks contains an invalid CIDR block: {knownIpNetwork}");
                }

                options.KnownIPNetworks.Add(parsedNetwork);
            }
        });
    }

    public static AppSettings RegisterConfiguration(this WebApplicationBuilder builder, string[] args, bool validateOnStart = true)
    {
        RegisterConfigurationSources(builder, args);

        builder.Services.Configure<AppSettings>(builder.Configuration);
        builder.Services.Configure<ConnectionStrings>(builder.Configuration.GetSection(nameof(AppSettings.ConnectionStrings)));

        AppSettings appSettings = new AppSettings
        {
            ConfigurationBase = builder.Configuration
        };

        // Bind the app settings to the model
        builder.Configuration.Bind(appSettings);

        // Adds the Fluent Validation to DI.
        builder.Services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);

        // Validate the app settings model
        OptionsBuilder<AppSettings> appSettingsOptions = builder.Services
            .AddOptions<AppSettings>()
            .Bind(builder.Configuration)
            .ValidateDataAnnotations()
            .ValidateFluently();

        if (validateOnStart)
        {
            appSettingsOptions.ValidateOnStart();
        }

        builder.Services.AddSingleton(builder.Configuration);
        builder.Services.AddSingleton(appSettings);

        return appSettings;
    }

    internal static string ResolveRedactionKeyMaterial(string? configuredRedactionKey)
    {
        if (string.IsNullOrWhiteSpace(configuredRedactionKey) || IsPlaceholderValue(configuredRedactionKey))
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        string trimmedKey = configuredRedactionKey.Trim();

        try
        {
            byte[] decodedKey = Convert.FromBase64String(trimmedKey);
            if (decodedKey.Length >= 32)
            {
                return trimmedKey;
            }
        }
        catch (FormatException)
        {
            // Fall back to deriving a stable 32-byte key from a passphrase-like value.
        }

        byte[] derivedKey = SHA256.HashData(Encoding.UTF8.GetBytes(trimmedKey));
        return Convert.ToBase64String(derivedKey);
    }

    private static bool IsPlaceholderValue(string value)
    {
        return value.Contains("Replace-Key", StringComparison.OrdinalIgnoreCase)
               || value.Contains("__SET_", StringComparison.OrdinalIgnoreCase);
    }

    private static void RegisterConfigurationSources(WebApplicationBuilder builder, string[] args)
    {
        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false, true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true);

        if (!builder.Environment.IsProduction())
        {
            builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), true, true);
        }

        string keyVaultUri = builder.Configuration.GetValue<string>("KeyVaultUri")!;
        if (!string.IsNullOrEmpty(keyVaultUri) && !keyVaultUri.Contains("Replace-Key", StringComparison.CurrentCultureIgnoreCase))
        {
            builder.Configuration.AddAzureKeyVault(
                new Uri(builder.Configuration.GetValue<string>("KeyVaultUri")!),
                new DefaultAzureCredential());
        }

        builder.Configuration
            .AddEnvironmentVariables()
            .AddCommandLine(args);
    }

    private static void SetupLoggingAndTelemetry(WebApplicationBuilder builder, AppSettings appSettings)
    {
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

        if (HasConfiguredApplicationInsights(appSettings.ConnectionStrings.ApplicationInsights))
        {
            builder.Services.AddApplicationInsightsTelemetry(o =>
            {
                o.ConnectionString = appSettings.ConnectionStrings.ApplicationInsights;
            });
        }

        if (builder.Environment.IsDevelopment())
        {
            builder.Logging
                .AddConsole()
                .AddJsonConsole(o => o.JsonWriterOptions = new JsonWriterOptions
                {
                    Indented = true,
                    Encoder = JavaScriptEncoder.Default
                })
                .AddDebug();
        }

        ConfigureOpenTelemetry(builder, appSettings);

        builder.Services.AddHttpLogging(o =>
        {
            o.CombineLogs = true;
        });

        // Enabled Redaction on tagged model properties to avoid logging sensitive data to telemetry and logs
        builder.Logging.EnableRedaction();
        builder.Services.AddRedaction(x =>
        {
            string normalizedRedactionKey = ResolveRedactionKeyMaterial(appSettings.RedactionKey);

            // This Redactor will erase the data leaving it blank.
            x.SetRedactor<ErasingRedactor>(new DataClassificationSet(DataTaxonomy.SensitiveData));
            // This Redactor will replace the data with asterisks preserving the length of the original data.
            x.SetRedactor<StarRedactor>(new DataClassificationSet(DataTaxonomy.PartialSensitiveData));

            // This Redactor will encrypt the data using HMAC with a key and keyId.
            x.SetHmacRedactor(o =>
            {
                o.Key = normalizedRedactionKey;
                o.KeyId = 1830;
            }, new DataClassificationSet(DataTaxonomy.Pii));

            // This Redactor will not redact the data, but it will still be logged.
            x.SetFallbackRedactor<NullRedactor>();

            builder.Services.AddControllersWithViews(options =>
            {
                //options.Filters.Add<CustomExceptionFilterAttribute>();
            }).AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                options.SerializerSettings.Formatting = Formatting.Indented;
                options.SerializerSettings.Converters.Add(new StringEnumConverter());
                options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
            });
        });

        // Add problem details for error handling on API endpoints and logging
        builder.Services.AddProblemDetails(o =>
        {
            o.CustomizeProblemDetails = context =>
            {
                RequestTraceContext.EnrichProblemDetails(context.HttpContext, context.ProblemDetails);
            };
        });

        builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
    }

    private static void ConfigureOpenTelemetry(WebApplicationBuilder builder, AppSettings appSettings)
    {
        if (!appSettings.FeatureManagement.OpenTelemetryEnabled)
        {
            return;
        }

        Uri? aspireEndpoint = appSettings.FeatureManagement.AspireEnabled
            ? ResolveAspireOtlpEndpoint(builder.Configuration)
            : null;
        OtlpExportProtocol aspireProtocol = ResolveAspireOtlpProtocol(builder.Configuration);
        Uri? seqLogsEndpoint = appSettings.FeatureManagement.OpenTelemetrySeqEnabled
            ? LoggerSupport.BuildOtlpSignalEndpoint(appSettings.OpenTelemetry.Endpoint, "logs")
            : null;
        Uri? seqTracesEndpoint = appSettings.FeatureManagement.OpenTelemetrySeqEnabled
            ? LoggerSupport.BuildOtlpSignalEndpoint(appSettings.OpenTelemetry.Endpoint, "traces")
            : null;
        Uri? seqMetricsEndpoint = appSettings.FeatureManagement.OpenTelemetrySeqEnabled
            ? LoggerSupport.BuildOtlpSignalEndpoint(appSettings.OpenTelemetry.Endpoint, "metrics")
            : null;
        string seqHeaders = LoggerSupport.BuildSeqApiKeyHeader(appSettings.OpenTelemetry.ApiKey);

        LogLevel openTelemetryLogLevel = appSettings.OpenTelemetry.ExportDebugLogs ? LogLevel.Debug : LogLevel.Information;
        builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(category: null, level: openTelemetryLogLevel);

        if (appSettings.OpenTelemetry.ExportDebugLogs)
        {
            builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("Microsoft.AspNetCore", LogLevel.Information);
            builder.Logging.AddFilter<OpenTelemetryLoggerProvider>("Microsoft.Hosting.Lifetime", LogLevel.Information);
        }

        builder.Logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(CreateOpenTelemetryResourceBuilder(builder));
            options.IncludeScopes = appSettings.OpenTelemetry.IncludeScopes;
            options.IncludeFormattedMessage = appSettings.OpenTelemetry.IncludeFormattedMessage;
            options.ParseStateValues = appSettings.OpenTelemetry.ParseStateValues;

            if (builder.Environment.IsDevelopment())
            {
                options.AddConsoleExporter();
            }

            if (aspireEndpoint is not null)
            {
                options.AddOtlpExporter("aspire-logs", otlpOptions =>
                {
                    ConfigureOtlpExporter(otlpOptions, aspireEndpoint, aspireProtocol);
                });
            }

            if (seqLogsEndpoint is not null)
            {
                options.AddOtlpExporter("seq-logs", otlpOptions =>
                {
                    ConfigureOtlpExporter(otlpOptions, seqLogsEndpoint, OtlpExportProtocol.HttpProtobuf, seqHeaders);
                });
            }
        });

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => ConfigureOpenTelemetryResource(resource, builder))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(_activitySource.Name)
                    .AddSource(builder.Environment.ApplicationName)
                    .SetSampler(new AlwaysOnSampler())
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation();

                if (aspireEndpoint is not null)
                {
                    tracing.AddOtlpExporter("aspire-traces", otlpOptions =>
                    {
                        ConfigureOtlpExporter(otlpOptions, aspireEndpoint, aspireProtocol);
                    });
                }

                if (seqTracesEndpoint is not null)
                {
                    tracing.AddOtlpExporter("seq-traces", otlpOptions =>
                    {
                        ConfigureOtlpExporter(otlpOptions, seqTracesEndpoint, OtlpExportProtocol.HttpProtobuf, seqHeaders);
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                    .AddRuntimeInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddAspNetCoreInstrumentation();

                if (aspireEndpoint is not null)
                {
                    metrics.AddOtlpExporter("aspire-metrics", (otlpOptions, readerOptions) =>
                    {
                        ConfigureOtlpExporter(otlpOptions, aspireEndpoint, aspireProtocol);
                        readerOptions.TemporalityPreference = MetricReaderTemporalityPreference.Delta;
                    });
                }

                if (seqMetricsEndpoint is not null)
                {
                    metrics.AddOtlpExporter("seq-metrics", (otlpOptions, readerOptions) =>
                    {
                        ConfigureOtlpExporter(otlpOptions, seqMetricsEndpoint, OtlpExportProtocol.HttpProtobuf, seqHeaders);
                        readerOptions.TemporalityPreference = MetricReaderTemporalityPreference.Delta;
                    });
                }
            });
    }

    private static ResourceBuilder CreateOpenTelemetryResourceBuilder(WebApplicationBuilder builder)
    {
        return ConfigureOpenTelemetryResource(ResourceBuilder.CreateDefault(), builder);
    }

    private static ResourceBuilder ConfigureOpenTelemetryResource(ResourceBuilder resource, WebApplicationBuilder builder)
    {
        return resource
            .AddService(
                ResolveOpenTelemetryServiceName(builder),
                autoGenerateServiceInstanceId: false)
            .AddAttributes(new Dictionary<string, object>()
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName,
                ["deployment.version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0"
            });
    }

    private static string ResolveOpenTelemetryServiceName(WebApplicationBuilder builder)
    {
        string? configuredServiceName = builder.Configuration["OTEL_SERVICE_NAME"]
            ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");

        if (!string.IsNullOrWhiteSpace(configuredServiceName))
        {
            return configuredServiceName;
        }

        string? serviceNameAttribute = TryGetResourceAttributeValue(
            builder.Configuration["OTEL_RESOURCE_ATTRIBUTES"] ?? Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES"),
            "service.name");

        return !string.IsNullOrWhiteSpace(serviceNameAttribute)
            ? serviceNameAttribute
            : Assembly.GetEntryAssembly()?.GetName().Name ?? builder.Environment.ApplicationName;
    }

    private static string? TryGetResourceAttributeValue(string? resourceAttributes, string key)
    {
        if (string.IsNullOrWhiteSpace(resourceAttributes))
        {
            return null;
        }

        foreach (string attribute in resourceAttributes.Split(','))
        {
            int separatorIndex = attribute.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string attributeKey = attribute[..separatorIndex].Trim();
            if (!string.Equals(attributeKey, key, StringComparison.Ordinal))
            {
                continue;
            }

            string value = attribute[(separatorIndex + 1)..].Trim();
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }

        return null;
    }

    private static Uri? ResolveAspireOtlpEndpoint(IConfiguration configuration)
    {
        string? endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        return Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            ? uri
            : null;
    }

    private static OtlpExportProtocol ResolveAspireOtlpProtocol(IConfiguration configuration)
    {
        string? protocol = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL");

        return protocol?.Trim().ToLowerInvariant() switch
        {
            "http/protobuf" or "http_protobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => OtlpExportProtocol.Grpc
        };
    }

    private static void ConfigureOtlpExporter(
        OtlpExporterOptions options,
        Uri endpoint,
        OtlpExportProtocol protocol,
        string? headers = null)
    {
        options.Endpoint = endpoint;
        options.Protocol = protocol;
        options.ExportProcessorType = OpenTelemetry.ExportProcessorType.Batch;

        if (!string.IsNullOrWhiteSpace(headers))
        {
            options.Headers = headers;
        }
    }

    private static void SetDependencyInjection(this WebApplicationBuilder builder, AppSettings settings)
    {
        //http client wrapper etc
        ConnectionResolver.RegisterDependencies(builder.Services);

        // services
        ServicesResolver.RegisterDependencies(builder.Services);

        // helpers
        HelpersResolver.RegisterDependencies(builder.Services);

        // repositories
        RepositoriesResolver.RegisterDependencies(builder.Services, settings);

        //factories
        FactoriesResolver.RegisterDependencies(builder.Services);
    }

    private static void ConfigureDistributedCaching(WebApplicationBuilder builder, AppSettings appSettings)
    {
        builder.Services.Configure<CacheSettings>(builder.Configuration.GetSection(nameof(AppSettings.Cache)));
        builder.Services.Configure<RedisHealthMonitorOptions>(builder.Configuration.GetSection("Cache:HealthMonitor"));

        builder.Services.AddMemoryCache();
        if (!ShouldUseRedisCaching(appSettings))
        {
            builder.Services.AddDistributedMemoryCache();
            return;
        }

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(CreateRedisConfigurationOptions(appSettings.Redis)));
        builder.Services.AddSingleton<RedisCache>(sp =>
            new RedisCache(
                Options.Create(new RedisCacheOptions
                {
                    ConnectionMultiplexerFactory = () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>()),
                    InstanceName = appSettings.Redis.InstanceName
                })));
        builder.Services.AddSingleton<RedisHealthState>(sp =>
            CreateRedisHealthState(sp.GetRequiredService<IConnectionMultiplexer>(), appSettings.Redis.Url, sp.GetRequiredService<ILogger<RedisHealthState>>()));
        builder.Services.AddHostedService<RedisHealthMonitor>();
        builder.Services.AddSingleton<IDistributedCache>(sp =>
        {
            return new ResilientDistributedCache(
                sp.GetRequiredService<RedisCache>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<RedisHealthState>(),
                sp.GetRequiredService<ILogger<ResilientDistributedCache>>());
        });
    }

    private static bool ShouldUseRedisCaching(AppSettings appSettings)
    {
        return appSettings.Redis is not null &&
               !appSettings.Redis.LocalOverride &&
               !string.IsNullOrWhiteSpace(appSettings.Redis.Url);
    }

    private static void ConfigureAuthorization(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthorization(SystemUptimeTrackerAuthorizationPolicyCatalog.Configure);
    }

    private static async Task ValidateLocalJwtAccountAsync(TokenValidatedContext context)
    {
        string? accountId = SystemUptimeTrackerAuthorizationClaims.ResolveSignedInAccountId(context.Principal);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            context.Fail("The JWT does not identify a local account.");
            return;
        }

        UserManager<ApplicationUser> userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser? user = await userManager.FindByIdAsync(accountId);
        if (user is null || !user.IsActive)
        {
            context.Fail("The JWT does not identify an active local account.");
        }
    }

    private static RedisHealthState CreateRedisHealthState(
        IConnectionMultiplexer multiplexer,
        string redisUrl,
        ILogger<RedisHealthState> logger)
    {
        var state = new RedisHealthState(logger, multiplexer.IsConnected);

        if (!multiplexer.IsConnected)
        {
            logger.LogWarning(
                "Redis is offline at application startup. Falling back to in-memory cache until connectivity is restored. Configured Redis endpoint: {RedisUrl}",
                redisUrl);
        }

        return state;
    }

    private static ConfigurationOptions CreateRedisConfigurationOptions(Redis redisSettings)
    {
        var configurationOptions = ConfigurationOptions.Parse(redisSettings.Url);
        configurationOptions.AbortOnConnectFail = false;
        configurationOptions.ConnectRetry = 1;
        configurationOptions.ConnectTimeout = 5000;
        configurationOptions.AsyncTimeout = 5000;
        configurationOptions.SyncTimeout = 5000;

        return configurationOptions;
    }

    internal class ApiInfo
    {
        public Version? GetAssemblyVersion()
        {
            return GetType().Assembly.GetName().Version;
        }
    }
}
