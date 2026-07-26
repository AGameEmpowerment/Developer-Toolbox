using SystemUptimeTracker.Common.Connection.Interfaces;

namespace SystemUptimeTracker.Api.Repositories;

public abstract class BaseRepository
{
    // ReSharper disable once ConvertToPrimaryConstructor
    protected BaseRepository(IHttpClientWrapper httpClientWrapper)
    {
        HttpClientWrapper = httpClientWrapper;
    }

    protected IHttpClientWrapper HttpClientWrapper { get; }
}