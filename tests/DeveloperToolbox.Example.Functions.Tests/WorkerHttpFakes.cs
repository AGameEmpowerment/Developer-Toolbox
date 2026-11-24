using System.Net;
using System.Security.Claims;
using Azure.Core.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DeveloperToolbox.Example.Functions.Tests;

internal sealed class TestFunctionContext : FunctionContext, IDisposable
{
    private readonly ServiceProvider services = new ServiceCollection()
        .Configure<WorkerOptions>(options => options.Serializer = new JsonObjectSerializer())
        .BuildServiceProvider();
    public TestFunctionContext() { InstanceServices = services; }
    public override string InvocationId => "test-invocation";
    public override string FunctionId => "test-function";
    public override TraceContext TraceContext => throw new NotSupportedException();
    public override BindingContext BindingContext => throw new NotSupportedException();
    public override RetryContext RetryContext => throw new NotSupportedException();
    public override IServiceProvider InstanceServices { get; set; }
    public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();
    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
    public override IInvocationFeatures Features => throw new NotSupportedException();
    public void Dispose() => services.Dispose();
}

internal sealed class TestHttpRequest(FunctionContext context) : HttpRequestData(context)
{
    public override Stream Body { get; } = new MemoryStream();
    public override HttpHeadersCollection Headers { get; } = new();
    public override IReadOnlyCollection<IHttpCookie> Cookies => [];
    public override Uri Url => new("http://localhost/api/samples/test");
    public override IEnumerable<ClaimsIdentity> Identities => [];
    public override string Method => "POST";
    public override HttpResponseData CreateResponse() => new TestHttpResponse(FunctionContext);
}

internal sealed class TestHttpResponse(FunctionContext context) : HttpResponseData(context)
{
    public override HttpStatusCode StatusCode { get; set; }
    public override HttpHeadersCollection Headers { get; set; } = new();
    public override Stream Body { get; set; } = new MemoryStream();
    public override HttpCookies Cookies => throw new NotSupportedException();
}
