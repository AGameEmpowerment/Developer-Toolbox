using System.Text.Json;
using DeveloperToolbox.Example.App;
using NUnit.Framework;

namespace DeveloperToolbox.Example.App.Tests;

public sealed class LiveValidationTests
{
    [Test]
    public void TryParse_NoResources_SelectsOnlyInfrastructureResources()
    {
        Assert.That(LiveValidation.TryParse([], out var options, out var error), Is.True);
        Assert.That(options!.Resources, Is.EqualTo(SampleApplication.ResourceNames));
        Assert.That(options.Resources.Count, Is.EqualTo(18));
        Assert.That(options.Resources, Does.Not.Contain("functions"));
        Assert.That(options.Resources, Does.Not.Contain("lambda"));
        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(45)));
        Assert.That(options.Json, Is.False);
        Assert.That(error, Is.Empty);
    }

    [Test]
    public void TryParse_SelectedResources_NormalizesAndDeduplicatesInRequestedOrder()
    {
        Assert.That(LiveValidation.TryParse(["REDIS", "--json", "s3", "redis", "--timeout-seconds", "7"], out var options, out _), Is.True);
        Assert.That(options!.Resources, Is.EqualTo(new[] { "redis", "s3" }));
        Assert.That(options.Json, Is.True);
        Assert.That(options.Timeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [TestCase("")]
    [TestCase("0")]
    [TestCase("301")]
    [TestCase("-1")]
    [TestCase("1.5")]
    [TestCase("private-setting")]
    public void TryParse_InvalidTimeout_ReturnsSafeUsageError(string value)
    {
        Assert.That(LiveValidation.TryParse(["--timeout-seconds", value], out var options, out var error), Is.False);
        Assert.That(options, Is.Null);
        Assert.That(error, Does.Contain("whole number"));
        Assert.That(error, Does.Not.Contain("private-setting"));
    }

    [TestCase("--timeout-seconds")]
    [TestCase("unknown-private-setting")]
    [TestCase("--unexpected")]
    [TestCase("functions")]
    [TestCase("lambda")]
    public async Task RunAsync_InvalidValidationArguments_DoesNotConstructClients(string argument)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await SampleApplication.RunAsync(["validate", argument],
            () => throw new Exception("Unexpected client initialization."), output, error);
        Assert.That(code, Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Is.Not.Empty);
        Assert.That(error.ToString(), Does.Not.Contain("private-setting"));
    }

    [Test]
    public async Task RunAsync_AllChecksPass_ReturnsSuccessWithoutPrintingProviderResults()
    {
        var calls = new List<string>();
        using var output = new StringWriter();
        var options = new ValidationOptions(LiveValidation.ResourceNames, false, TimeSpan.FromSeconds(5));
        var code = await LiveValidation.RunAsync(options, (name, token) =>
        {
            Assert.That(token.CanBeCanceled, Is.True);
            calls.Add(name);
            return Task.FromResult("provider-result-that-must-not-be-printed");
        }, output);
        Assert.That(code, Is.EqualTo(0));
        Assert.That(calls, Is.EqualTo(LiveValidation.ResourceNames));
        Assert.That(output.ToString(), Does.Contain("18 passed, 0 failed"));
        Assert.That(output.ToString(), Does.Not.Contain("provider-result"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RunAsync_OneFailure_ContinuesAndKeepsDiagnosticsSecretSafe(bool json)
    {
        var calls = new List<string>();
        using var output = new StringWriter();
        var options = new ValidationOptions(["redis", "s3", "wiremock"], json, TimeSpan.FromSeconds(5));
        var code = await LiveValidation.RunAsync(options, (name, _) =>
        {
            calls.Add(name);
            return name == "s3" ? Task.FromException<string>(new InvalidOperationException("key=private-diagnostic"))
                : Task.FromResult("private-provider-output");
        }, output);
        Assert.That(code, Is.EqualTo(1));
        Assert.That(calls, Is.EqualTo(options.Resources));
        Assert.That(output.ToString(), Does.Contain("InvalidOperationException"));
        Assert.That(output.ToString(), Does.Not.Contain("private-"));
        if (json)
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.That(document.RootElement.GetProperty("passed").GetInt32(), Is.EqualTo(2));
            Assert.That(document.RootElement.GetProperty("failed").GetInt32(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("checks")[1].GetProperty("status").GetString(), Is.EqualTo("failed"));
        }
    }

    [Test]
    public async Task RunAsync_CheckTimeout_AwaitsCleanupBeforeNextCheck()
    {
        var cleaned = false;
        using var output = new StringWriter();
        var options = new ValidationOptions(["redis", "wiremock"], true, TimeSpan.FromMilliseconds(25));
        var code = await LiveValidation.RunAsync(options, async (name, token) =>
        {
            if (name == "redis")
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                finally { await Task.Yield(); cleaned = true; }
            }
            Assert.That(cleaned, Is.True);
            return "done";
        }, output);
        Assert.That(code, Is.EqualTo(1));
        using var document = JsonDocument.Parse(output.ToString());
        Assert.That(document.RootElement.GetProperty("checks")[0].GetProperty("status").GetString(), Is.EqualTo("timed_out"));
        Assert.That(document.RootElement.GetProperty("checks")[1].GetProperty("status").GetString(), Is.EqualTo("passed"));
    }

    [Test]
    public async Task RunAsync_CallerAlreadyCanceled_ReportsUnexecutedChecks()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        using var output = new StringWriter();
        var code = await LiveValidation.RunAsync(new ValidationOptions(["redis", "wiremock"], true, TimeSpan.FromSeconds(5)),
            (_, _) => throw new Exception("Canceled checks must not run."), output, stop.Token);
        Assert.That(code, Is.EqualTo(1));
        using var document = JsonDocument.Parse(output.ToString());
        Assert.That(document.RootElement.GetProperty("checks").EnumerateArray()
            .Select(check => check.GetProperty("status").GetString()), Is.All.EqualTo("canceled"));
    }

    [Test]
    public async Task RunAsync_ProviderReturnsAfterCallerCancellation_DoesNotReportSuccess()
    {
        using var stop = new CancellationTokenSource();
        using var output = new StringWriter();
        var calls = 0;
        var code = await LiveValidation.RunAsync(
            new ValidationOptions(["redis", "wiremock"], true, TimeSpan.FromSeconds(5)),
            (_, _) =>
            {
                calls++;
                stop.Cancel();
                return Task.FromResult("completed after cancellation");
            }, output, stop.Token);

        Assert.That(code, Is.EqualTo(1));
        Assert.That(calls, Is.EqualTo(1));
        using var document = JsonDocument.Parse(output.ToString());
        Assert.That(document.RootElement.GetProperty("checks").EnumerateArray()
            .Select(check => check.GetProperty("status").GetString()), Is.All.EqualTo("canceled"));
    }

    [Test]
    public async Task RunAsync_ProviderCancelsWithoutBudgetExpiration_ReportsFailure()
    {
        using var output = new StringWriter();
        var code = await LiveValidation.RunAsync(new ValidationOptions(["redis"], true, TimeSpan.FromSeconds(5)),
            (_, _) => Task.FromException<string>(new OperationCanceledException()), output);
        Assert.That(code, Is.EqualTo(1));
        using var document = JsonDocument.Parse(output.ToString());
        Assert.That(document.RootElement.GetProperty("checks")[0].GetProperty("status").GetString(), Is.EqualTo("failed"));
    }
}
