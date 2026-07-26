using SystemUptimeTracker.Api.Models.Operations;

namespace SystemUptimeTracker.Api.Services.Operations.Interface;

public interface IOperationsMetadataService
{
    OperationsMetadataResponse GetMetadata();
}