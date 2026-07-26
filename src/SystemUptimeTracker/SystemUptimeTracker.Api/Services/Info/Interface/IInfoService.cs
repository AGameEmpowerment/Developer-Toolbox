namespace SystemUptimeTracker.Api.Services.Info.Interface;

public interface IInfoService
{
    string SerializeToResponseXml();
    string? SerializeToResponseJson();
}