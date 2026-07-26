using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SystemUptimeTracker.Api.Services.Operations;

namespace SystemUptimeTracker.Tests.Services.Operations;

[TestFixture(Category = "Unit")]
public class OperationsMetadataServiceTests
{
    [Test]
    public void GetMetadata_ReturnsSafeOperationalFields()
    {
        const string TESTING_ENVIRONMENT = "Testing";
        DateTimeOffset startedAtUtc = new DateTimeOffset(2026, 4, 17, 12, 0, 0, TimeSpan.Zero);

        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns(TESTING_ENVIRONMENT);
        hostEnvironment.ApplicationName.Returns("SystemUptimeTracker.Api");

        var service = new OperationsMetadataService(hostEnvironment, startedAtUtc, NullLogger<OperationsMetadataService>.Instance);

        var metadata = service.GetMetadata();

        Assert.Multiple(() =>
        {
            Assert.That(metadata.ApplicationName, Is.EqualTo("SystemUptimeTracker.Api"));
            Assert.That(metadata.ApplicationVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(metadata.BuildVersion, Is.Not.Null.And.Not.Empty);
            Assert.That(metadata.Environment, Is.EqualTo(TESTING_ENVIRONMENT));
            Assert.That(metadata.StartedAtUtc, Is.EqualTo(startedAtUtc));
        });
    }
}