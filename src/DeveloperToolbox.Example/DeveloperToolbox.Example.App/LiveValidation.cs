using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace DeveloperToolbox.Example.App;

/// <summary>Runs the examples as explicit live checks without managing container state.</summary>
internal static class LiveValidation
{
    internal static readonly IReadOnlyList<string> ResourceNames = SampleApplication.ResourceNames;

    internal static bool TryParse(IReadOnlyList<string> arguments, out ValidationOptions? options, out string error)
    {
        var names = new List<string>();
        var json = false;
        var timeout = 45;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index].ToLowerInvariant();
            if (argument == "--json")
            {
                json = true;
            }
            else if (argument == "--timeout-seconds")
            {
                if (++index >= arguments.Count || !int.TryParse(arguments[index], NumberStyles.None,
                    CultureInfo.InvariantCulture, out timeout) || timeout is < 1 or > 300)
                {
                    options = null;
                    error = "--timeout-seconds requires a whole number from 1 to 300.";
                    return false;
                }
            }
            else if (ResourceNames.Contains(argument))
            {
                if (!names.Contains(argument)) { names.Add(argument); }
            }
            else
            {
                options = null;
                error = "Unknown validation resource or option. Use --help for supported names.";
                return false;
            }
        }
        options = new ValidationOptions(names.Count == 0 ? ResourceNames : names.AsReadOnly(),
            json, TimeSpan.FromSeconds(timeout));
        error = "";
        return true;
    }

    /// <summary>Awaits canceled operations and their cleanup before moving to the next check.</summary>
    internal static async Task<int> RunAsync(ValidationOptions options,
        Func<string, CancellationToken, Task<string>> execute, TextWriter output,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ValidationResult>();
        foreach (var resource in options.Resources)
        {
            var watch = Stopwatch.StartNew();
            var status = "passed";
            string? error = null;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(options.Timeout);
            try
            {
                budget.Token.ThrowIfCancellationRequested();
                await execute(resource, budget.Token);
                // A provider that returns after cancellation has not completed within its budget.
                budget.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                status = "canceled";
            }
            catch (Exception) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                status = "timed_out";
                error = "TimeoutException";
            }
            catch (Exception exception)
            {
                // Provider diagnostics can include keys, passwords, URLs, and payloads.
                status = "failed";
                error = exception.GetType().Name;
            }
            watch.Stop();
            var result = new ValidationResult(resource, status, watch.ElapsedMilliseconds, error);
            results.Add(result);
            if (!options.Json)
            {
                var label = status switch { "passed" => "PASS", "timed_out" => "TIMEOUT", "canceled" => "CANCELED", _ => "FAIL" };
                await output.WriteLineAsync($"[{label}] {resource} ({result.DurationMilliseconds} ms){(error is null ? "" : $" {error}")}");
            }
        }
        var passed = results.Count(result => result.Status == "passed");
        var failed = results.Count - passed;
        if (options.Json)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(new { passed, failed, checks = results },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
        else
        {
            await output.WriteLineAsync($"Validation: {passed} passed, {failed} failed or canceled.");
        }
        return failed == 0 ? 0 : 1;
    }
}

internal sealed record ValidationOptions(IReadOnlyList<string> Resources, bool Json, TimeSpan Timeout);
internal sealed record ValidationResult(string Resource, string Status, long DurationMilliseconds, string? Error);
