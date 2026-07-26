using Microsoft.Extensions.Options;
using System.Net.Security;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    internal sealed class ApiClientFactory : IApiClientFactory, IDisposable
    {
        private readonly AutomationAppSettings _appSettings;
        private readonly HttpClientHandler _handler;

        public ApiClientFactory(IOptions<AutomationAppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
            _handler = CreateHandler(_appSettings.ApiClientConfiguration.AllowLoopbackCertificateBypass);
        }

        public HttpClient InitHttpClient(string acceptHeader)
        {
            var client = new HttpClient(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri(_appSettings.BaseUrl),
                Timeout = TimeSpan.FromSeconds(_appSettings.ApiClientConfiguration.TcpTimeoutInSeconds)
            };

            client.DefaultRequestHeaders.Accept.ParseAdd(acceptHeader);

            return client;
        }

        public void Dispose()
        {
            _handler.Dispose();
        }

        private static HttpClientHandler CreateHandler(bool allowLoopbackCertificateBypass)
        {
            return new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
                    errors == SslPolicyErrors.None ||
                    (allowLoopbackCertificateBypass &&
                     request?.RequestUri is { IsLoopback: true } &&
                     request.RequestUri.Scheme == Uri.UriSchemeHttps)
            };
        }
    }
}