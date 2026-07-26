using SystemUptimeTracker.Common.Helpers.Data;

namespace SystemUptimeTracker.Tests.Common;

[TestFixture]
public sealed class LoggerSupportTests
{
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("Replace-Key-From-Secrets.json")]
    [TestCase("replace_with_seq_api_key")]
    public void BuildSeqApiKeyHeader_WhenKeyIsMissingOrPlaceholder_ReturnsEmptyHeader(string apiKey)
    {
        string header = LoggerSupport.BuildSeqApiKeyHeader(apiKey);

        Assert.That(header, Is.Empty);
    }

    [Test]
    public void BuildSeqApiKeyHeader_WhenKeyIsConfigured_ReturnsSeqHeader()
    {
        string header = LoggerSupport.BuildSeqApiKeyHeader(" local-dev-key ");

        Assert.That(header, Is.EqualTo("X-Seq-ApiKey=local-dev-key"));
    }
}
