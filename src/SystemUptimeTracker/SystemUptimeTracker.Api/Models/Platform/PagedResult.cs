namespace SystemUptimeTracker.Api.Models.Platform;

public class PagedResult<T>
{
    public List<T> Results { get; set; } = [];
    public int TotalResults { get; set; }
    public List<PagedLink> Links { get; set; } = [];
}
