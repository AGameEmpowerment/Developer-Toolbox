using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Loader;
using System.Text;

namespace SystemUptimeTracker.Qa.Automation.Infrastructure;

internal static class SystemUptimeTrackerAppHostManager
{
    private const string DEFAULT_CONNECTION_ENVIRONMENT_VARIABLE = "ConnectionStrings__DefaultConnection";
    private const int DEFAULT_APP_HOST_PORT = 18888;
    private const int DEFAULT_SERVER_PORT = 7060;
    private const int DEFAULT_CLIENT_PORT = 3001;
    private const int DEFAULT_ASPIRE_DASHBOARD_OTLP_GRPC_PORT = 4317;
    private const int DEFAULT_ASPIRE_DASHBOARD_OTLP_HTTP_PORT = 4318;
    private const string SERVER_BASE_URL = "https://localhost:7060";
    private const string SERVER_READINESS_PATH = "/_health";
    private const string CLIENT_BASE_URL = "https://localhost:3001";
    private const string DISTRIBUTED_APPLICATION_STARTED_MESSAGE = "Distributed application started. Press Ctrl+C to shut down.";
    private const string APP_HOST_TEMP_OWNER_FILE_NAME = ".owner.pid";
    private const int STALE_DIRECTORY_CLEANUP_MAX_COUNT = 5;
    private const int STALE_DIRECTORY_CLEANUP_MAX_ATTEMPTS = 2;
    private const int STALE_DIRECTORY_CLEANUP_RETRY_DELAY_MILLISECONDS = 100;
    private static readonly TimeSpan _startupTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _stallTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan _spawnedProcessAdoptionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _portShutdownTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan _staleAppHostTempDirectoryAgeThreshold = TimeSpan.FromMinutes(15);
    private static readonly object _syncLock = new();
    private static readonly object _outputLock = new();
    private static readonly StringBuilder _output = new();
    private const string CLIENT_RELATIVE_PATH = "src\\SystemUptimeTracker\\SystemUptimeTracker.Web";
    private const string NEXT_CLI_RELATIVE_PATH = "node_modules\\next\\dist\\bin\\next";
    private const string APP_HOST_EXECUTABLE_RELATIVE_PATH = "src\\SystemUptimeTracker\\SystemUptimeTracker.AppHost\\bin\\Debug\\net10.0\\SystemUptimeTracker.AppHost.exe";
    private const string API_EXECUTABLE_RELATIVE_PATH = "src\\SystemUptimeTracker\\SystemUptimeTracker.Api\\bin\\Debug\\net10.0\\SystemUptimeTracker.Api.exe";
    private const string APP_HOST_PROCESS_NAME = "SystemUptimeTracker.AppHost";
    private const string API_PROCESS_NAME = "SystemUptimeTracker.Api";
    private static readonly string[] _apiStartupLogPatterns =
    [
        "systemuptimetracker-api*_err_*",
        "systemuptimetracker-api*_out_*"
    ];
    private static readonly string[] _clientStartupLogPatterns =
    [
        "systemuptimetracker-web-installer*_err_*",
        "systemuptimetracker-web-installer*_out_*",
        "systemuptimetracker-web*_err_*",
        "systemuptimetracker-web*_out_*"
    ];
    private static readonly string[] _fatalStartupLogMarkers =
    [
        "Unhandled exception.",
        "Microsoft.Extensions.Options.OptionsValidationException",
        "System.InvalidOperationException:",
        "Failed to compile",
        "npm ERR!",
        "Error: listen",
        "Error: Cannot",
        "net::ERR_"
    ];
    private static Process? _appHostProcess;
    private static int _usageCount;
    private static bool _isReady;
    private static SystemUptimeTrackerAppHostReadinessScope _readyScope;
    private static bool _distributedApplicationStarted;
    private static bool _dashboardReady;
    private static DateTime _lastProgressUtc;
    private static string? _appHostTempPath;
    private static string? _repoRoot;
    private static bool _cleanupHooksRegistered;
    private static DateTime _startupAttemptUtc;
    private readonly record struct ProcessExecutionResult(bool Started, int ExitCode, string StandardOutput, string StandardError);
    private readonly record struct StartupStateSnapshot(Process? AppHostProcess, bool DistributedApplicationStarted, bool DashboardReady, DateTime LastProgressUtc);

    internal static string[] CreateQaAutomationHostArgs()
    {
        return
        [
            $"--AppSettings:BaseUrl={SERVER_BASE_URL}/",
            "--AppSettings:BaseRoute=",
            $"--TestConfiguration:WebValidation:BaseUrl={CLIENT_BASE_URL}",
            "--FeatureManagement:AspireEnabled=true",
            "--FeatureManagement:OpenTelemetrySeqEnabled=true"
        ];
    }

    private static void EnsureRuntimePortsInitialized()
    {
    }

    private static int GetAppHostPort()
    {
        return DEFAULT_APP_HOST_PORT;
    }

    private static int GetServerPort()
    {
        return DEFAULT_SERVER_PORT;
    }

    private static int GetClientPort()
    {
        return DEFAULT_CLIENT_PORT;
    }

    private static int GetAspireDashboardOtlpGrpcPort()
    {
        return DEFAULT_ASPIRE_DASHBOARD_OTLP_GRPC_PORT;
    }

    private static int GetAspireDashboardOtlpHttpPort()
    {
        return DEFAULT_ASPIRE_DASHBOARD_OTLP_HTTP_PORT;
    }

    private static string GetServerBaseUrl()
    {
        return SERVER_BASE_URL;
    }

    private static string GetClientBaseUrl()
    {
        return CLIENT_BASE_URL;
    }

    private static string GetAppHostBaseUrl()
    {
        return CreateLoopbackHttpsUrl(GetAppHostPort());
    }

    private static string CreateLoopbackHttpsUrl(int port)
    {
        return string.Create(CultureInfo.InvariantCulture, $"https://localhost:{port}");
    }

    internal static string BuildNextAllowedDevOrigins(int clientPort)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"host.docker.internal,host.docker.internal:{clientPort},localhost,localhost:{clientPort},127.0.0.1,127.0.0.1:{clientPort}");
    }

    private static IReadOnlyCollection<int> GetManagedPorts()
    {
        return
        [
            GetAppHostPort(),
            GetServerPort(),
            GetClientPort(),
            GetAspireDashboardOtlpGrpcPort(),
            GetAspireDashboardOtlpHttpPort()
        ];
    }

    internal static void Acquire(SystemUptimeTrackerAppHostReadinessScope readinessScope = SystemUptimeTrackerAppHostReadinessScope.SERVER_AND_CLIENT)
    {
        EnsureCleanupHooksRegistered();

        bool startNewProcess = false;

        lock (_syncLock)
        {
            if (_isReady
                && _appHostProcess is { HasExited: false }
                && ReadinessScopeSatisfies(_readyScope, readinessScope))
            {
                _usageCount++;
                return;
            }

            startNewProcess = _appHostProcess is not { HasExited: false };
        }

        if (startNewProcess)
        {
            ForceCleanup();

            lock (_syncLock)
            {
                ClearOutput();
                _distributedApplicationStarted = false;
                _dashboardReady = false;
                _repoRoot ??= ResolveRepoRoot();

                // Clean up any stale AppHost temp directories before creating a new one.
                CleanupStaleAppHostTempDirectories(_repoRoot);
                CleanupManagedProcesses(_repoRoot);
                WaitForBuildOutputsUnlocked(_repoRoot);

                string appHostProjectPath = Path.Combine(
                    _repoRoot,
                    "src",
                    "SystemUptimeTracker",
                    "SystemUptimeTracker.AppHost",
                    "SystemUptimeTracker.AppHost.csproj");

                EnsureClientDependenciesInstalled(_repoRoot);

                _appHostTempPath = Path.Combine(_repoRoot, ".tmp", "apphost", Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(_appHostTempPath);
                WriteAppHostTempOwnerMarker(_appHostTempPath, Environment.ProcessId);

                string resolvedQaConnectionString = RegisterDependentServices.ResolveQaDatabaseConnectionString(SystemUptimeTrackerTestEnvironment.Resolve());

                var startInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = _repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                startInfo.ArgumentList.Add("run");
                startInfo.ArgumentList.Add("--project");
                startInfo.ArgumentList.Add(appHostProjectPath);
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("Debug");
                startInfo.ArgumentList.Add("--no-launch-profile");
                startInfo.ArgumentList.Add("--");
                startInfo.ArgumentList.Add($"--ConnectionStrings:DefaultConnection={resolvedQaConnectionString}");

                startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
                startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
                startInfo.Environment["ASPIRE_ALLOW_UNSECURED_TRANSPORT"] = "true";
                startInfo.Environment["ASPNETCORE_URLS"] = GetAppHostBaseUrl();
                startInfo.Environment["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"] =
                    string.Create(CultureInfo.InvariantCulture, $"http://localhost:{GetAspireDashboardOtlpGrpcPort()}");
                startInfo.Environment["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"] =
                    string.Create(CultureInfo.InvariantCulture, $"http://localhost:{GetAspireDashboardOtlpHttpPort()}");
                startInfo.Environment["ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS"] = "true";
                startInfo.Environment[DEFAULT_CONNECTION_ENVIRONMENT_VARIABLE] = resolvedQaConnectionString;
                startInfo.Environment["TMP"] = _appHostTempPath;
                startInfo.Environment["TEMP"] = _appHostTempPath;

                _appHostProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start the SystemUptimeTracker AppHost process.");
                _appHostProcess.OutputDataReceived += AppendOutput;
                _appHostProcess.ErrorDataReceived += AppendOutput;
                _appHostProcess.BeginOutputReadLine();
                _appHostProcess.BeginErrorReadLine();

                _startupAttemptUtc = DateTime.UtcNow;
                _lastProgressUtc = DateTime.UtcNow;
                _usageCount = 1;
                _readyScope = SystemUptimeTrackerAppHostReadinessScope.DASHBOARD_ONLY;
            }
        }
        else
        {
            lock (_syncLock)
            {
                _usageCount++;
            }
        }

        try
        {
            WaitForReadyAsync(readinessScope).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            ForceCleanup();
            throw;
        }

        lock (_syncLock)
        {
            _isReady = true;
            _readyScope = GetMaxReadinessScope(_readyScope, readinessScope);
        }
    }

    internal static void Release()
    {
        bool shouldCleanup;

        lock (_syncLock)
        {
            _usageCount = Math.Max(0, _usageCount - 1);
            shouldCleanup = _usageCount == 0;
        }

        if (shouldCleanup)
        {
            ForceCleanup();
        }
    }

    internal static void ForceCleanup()
    {
        Process? appHostProcess;
        string? appHostTempPath;
        string? repoRoot;

        lock (_syncLock)
        {
            appHostProcess = _appHostProcess;
            appHostTempPath = _appHostTempPath;
            repoRoot = _repoRoot;

            _appHostProcess = null;
            _appHostTempPath = null;
            _usageCount = 0;
            _isReady = false;
            _readyScope = SystemUptimeTrackerAppHostReadinessScope.DASHBOARD_ONLY;
            _distributedApplicationStarted = false;
            _dashboardReady = false;
            _startupAttemptUtc = default;
        }

        TryStopProcess(appHostProcess);

        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            CleanupManagedProcesses(repoRoot);
        }

        if (appHostProcess is not null || !string.IsNullOrWhiteSpace(repoRoot))
        {
            WaitForManagedPortsToClose();
        }

        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            WaitForBuildOutputsUnlocked(repoRoot);
        }

        CleanupTemporaryAppHostPath(appHostTempPath);
        ClearOutput();
    }

    private static async Task WaitForReadyAsync(SystemUptimeTrackerAppHostReadinessScope readinessScope)
    {
        DateTime deadline = DateTime.UtcNow.Add(_startupTimeout);
        using HttpClient client = CreateClient();

        while (DateTime.UtcNow < deadline)
        {
            if (await AppIsReadyAsync(client, readinessScope).ConfigureAwait(false))
            {
                return;
            }

            StartupStateSnapshot startupState = GetStartupStateSnapshot();

            if (startupState.AppHostProcess is { HasExited: true })
            {
                if (await TryAdoptSpawnedAppHostProcessAsync().ConfigureAwait(false))
                {
                    continue;
                }

                throw new InvalidOperationException($"SystemUptimeTracker AppHost exited early with code {startupState.AppHostProcess.ExitCode}. Output: {GetOutputSnapshot()}");
            }

            if (startupState.AppHostProcess is { HasExited: false }
                && ShouldAbortForStalledStartup(startupState.DistributedApplicationStarted, startupState.DashboardReady, startupState.LastProgressUtc, DateTime.UtcNow))
            {
                throw new TimeoutException($"SystemUptimeTracker AppHost stopped making progress for more than {_stallTimeout.TotalSeconds} seconds. Output: {GetOutputSnapshot()}");
            }

            if (TryGetStartupFailureDetails(readinessScope, out string? startupFailureDetails))
            {
                throw new InvalidOperationException(
                    $"SystemUptimeTracker AppHost child resource failed during startup. {startupFailureDetails} AppHost output: {GetOutputSnapshot()}");
            }

            await Task.Delay(750).ConfigureAwait(false);
        }

        throw new TimeoutException($"SystemUptimeTracker AppHost did not become ready within {_startupTimeout.TotalSeconds} seconds. Output: {GetOutputSnapshot()}");
    }

    private static async Task<bool> TryAdoptSpawnedAppHostProcessAsync()
    {
        DateTime deadline = DateTime.UtcNow.Add(_spawnedProcessAdoptionTimeout);

        do
        {
            if (TryAdoptSpawnedAppHostProcess())
            {
                return true;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    private static bool TryAdoptSpawnedAppHostProcess()
    {
        string? repoRoot;
        DateTime startupAttemptUtc;

        lock (_syncLock)
        {
            repoRoot = _repoRoot;
            startupAttemptUtc = _startupAttemptUtc;
        }

        if (string.IsNullOrWhiteSpace(repoRoot) || startupAttemptUtc == default)
        {
            return false;
        }

        string expectedExecutablePath = Path.Combine(repoRoot, APP_HOST_EXECUTABLE_RELATIVE_PATH);

        foreach (Process process in Process.GetProcessesByName(APP_HOST_PROCESS_NAME))
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.HasExited)
                {
                    continue;
                }

                string? processPath = process.MainModule?.FileName;
                if (!PathsReferToSameFile(processPath, expectedExecutablePath))
                {
                    continue;
                }

                if (process.StartTime.ToUniversalTime() < startupAttemptUtc.AddSeconds(-5))
                {
                    continue;
                }

                lock (_syncLock)
                {
                    _appHostProcess?.Dispose();
                    _appHostProcess = process;
                    _lastProgressUtc = DateTime.UtcNow;
                }

                return true;
            }
            catch
            {
                process.Dispose();
            }
        }

        return false;
    }

    private static async Task<bool> AppIsReadyAsync()
    {
        using HttpClient client = CreateClient();
        return await AppIsReadyAsync(client, SystemUptimeTrackerAppHostReadinessScope.SERVER_AND_CLIENT).ConfigureAwait(false);
    }

    private static async Task<bool> AppIsReadyAsync(HttpClient client, SystemUptimeTrackerAppHostReadinessScope readinessScope)
    {
        // Always probe the dashboard first so we can track whether AppHost itself is alive,
        // which disables the stall-abort guard even while child services are still compiling.
        bool dashboardUp = await EndpointIsReadyAsync(client, GetAppHostBaseUrl()).ConfigureAwait(false);
        if (dashboardUp)
        {
            lock (_syncLock)
            {
                _dashboardReady = true;
            }
        }

        if (readinessScope == SystemUptimeTrackerAppHostReadinessScope.DASHBOARD_ONLY)
        {
            return dashboardUp;
        }

        bool serverReady = await EndpointIsReadyAsync(client, $"{GetServerBaseUrl()}{SERVER_READINESS_PATH}").ConfigureAwait(false);
        if (!serverReady)
        {
            return false;
        }

        return !RequiresClientReadiness(readinessScope)
            || await EndpointIsReadyAsync(client, GetClientBaseUrl()).ConfigureAwait(false);
    }

    private static async Task<bool> EndpointIsReadyAsync(HttpClient client, string url)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            // The readiness probes only call the local AppHost endpoints, so keep the certificate bypass
            // scoped to loopback to avoid weakening TLS validation for any non-local requests.
            ServerCertificateCustomValidationCallback = static (request, _, _, errors) =>
                errors == SslPolicyErrors.None ||
                (request?.RequestUri is { IsLoopback: true } && request.RequestUri.Scheme == Uri.UriSchemeHttps)
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    private static void AppendOutput(object? sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            DateTime progressUtc = DateTime.UtcNow;

            lock (_syncLock)
            {
                if (IsDistributedApplicationStartedLine(e.Data))
                {
                    _distributedApplicationStarted = true;
                }

                _lastProgressUtc = progressUtc;
            }

            lock (_outputLock)
            {
                _output.AppendLine(e.Data);
            }
        }
    }

    private static StartupStateSnapshot GetStartupStateSnapshot()
    {
        lock (_syncLock)
        {
            return new StartupStateSnapshot(_appHostProcess, _distributedApplicationStarted, _dashboardReady, _lastProgressUtc);
        }
    }

    private static string GetOutputSnapshot()
    {
        lock (_outputLock)
        {
            return _output.ToString();
        }
    }

    private static void ClearOutput()
    {
        lock (_outputLock)
        {
            _output.Clear();
        }
    }

    private static void EnsureCleanupHooksRegistered()
    {
        lock (_syncLock)
        {
            if (_cleanupHooksRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += static (_, _) => ForceCleanup();
            AppDomain.CurrentDomain.UnhandledException += static (_, _) => ForceCleanup();
            AssemblyLoadContext.Default.Unloading += static _ => ForceCleanup();

            _cleanupHooksRegistered = true;
        }
    }

    internal static bool IsDistributedApplicationStartedLine(string? line)
    {
        return !string.IsNullOrWhiteSpace(line)
            && line.Contains(DISTRIBUTED_APPLICATION_STARTED_MESSAGE, StringComparison.Ordinal);
    }

    internal static bool RequiresClientReadiness(SystemUptimeTrackerAppHostReadinessScope readinessScope)
    {
        return readinessScope == SystemUptimeTrackerAppHostReadinessScope.SERVER_AND_CLIENT;
    }

    internal static bool ReadinessScopeSatisfies(SystemUptimeTrackerAppHostReadinessScope actualReadinessScope, SystemUptimeTrackerAppHostReadinessScope requiredReadinessScope)
    {
        return actualReadinessScope >= requiredReadinessScope;
    }

    internal static bool ShouldAbortForStalledStartup(bool distributedApplicationStarted, bool dashboardReady, DateTime lastProgressUtc, DateTime utcNow)
    {
        // Once the AppHost dashboard is confirmed alive, AppHost itself is healthy.
        // Silence after that point means child services (API/Web) are still compiling — not a stall.
        return !distributedApplicationStarted
            && !dashboardReady
            && utcNow - lastProgressUtc > _stallTimeout;
    }

    internal static bool PathsReferToSameFile(string? leftPath, string? rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
        {
            return false;
        }

        try
        {
            string normalizedLeftPath = Path.GetFullPath(leftPath);
            string normalizedRightPath = Path.GetFullPath(rightPath);

            return string.Equals(
                normalizedLeftPath,
                normalizedRightPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static SystemUptimeTrackerAppHostReadinessScope GetMaxReadinessScope(SystemUptimeTrackerAppHostReadinessScope left, SystemUptimeTrackerAppHostReadinessScope right)
    {
        return left >= right ? left : right;
    }

    private static string ResolveRepoRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    }

    private static void EnsureClientDependenciesInstalled(string repoRoot)
    {
        string clientDirectory = Path.Combine(repoRoot, CLIENT_RELATIVE_PATH);
        string nextCliPath = Path.Combine(clientDirectory, NEXT_CLI_RELATIVE_PATH);

        if (File.Exists(nextCliPath))
        {
            return;
        }

        ProcessExecutionResult installResult = RunProcess(
            GetNpmCommand(),
            "ci --no-audit --no-fund",
            clientDirectory);

        if (!installResult.Started)
        {
            throw new InvalidOperationException(
                $"Unable to install the SystemUptimeTracker.Web npm dependencies before launching the Aspire AppHost: {installResult.StandardError}");
        }

        if (installResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to install the SystemUptimeTracker.Web npm dependencies before launching the Aspire AppHost. ExitCode={installResult.ExitCode}. Output: {installResult.StandardOutput} {installResult.StandardError}");
        }

        if (!File.Exists(nextCliPath))
        {
            throw new InvalidOperationException(
                $"SystemUptimeTracker.Web npm restore completed but the Next.js CLI is still missing at '{nextCliPath}'.");
        }
    }

    private static void EnsureAppHostBuildOutputsAvailable(string repoRoot, string appHostProjectPath)
    {
        string appHostExecutablePath = Path.Combine(repoRoot, APP_HOST_EXECUTABLE_RELATIVE_PATH);
        string apiExecutablePath = Path.Combine(repoRoot, API_EXECUTABLE_RELATIVE_PATH);

        if (File.Exists(appHostExecutablePath) && File.Exists(apiExecutablePath))
        {
            return;
        }

        ProcessExecutionResult buildResult = RunProcess(
            "dotnet",
            $"build \"{appHostProjectPath}\" -c Debug --nologo",
            repoRoot);

        if (!buildResult.Started)
        {
            throw new InvalidOperationException(
                $"Unable to build the SystemUptimeTracker AppHost before launching QA automation: {buildResult.StandardError}");
        }

        if (buildResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to build the SystemUptimeTracker AppHost before launching QA automation. ExitCode={buildResult.ExitCode}. Output: {buildResult.StandardOutput} {buildResult.StandardError}");
        }

        if (!File.Exists(appHostExecutablePath) || !File.Exists(apiExecutablePath))
        {
            throw new InvalidOperationException(
                $"SystemUptimeTracker AppHost build completed but the expected binaries are missing. AppHost='{appHostExecutablePath}', Api='{apiExecutablePath}'.");
        }
    }

    private static string GetNpmCommand()
    {
        return OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
    }

    private static void TryStopProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)_portShutdownTimeout.TotalMilliseconds);
            }
        }
        catch
        {
            // Swallow cleanup failures to avoid masking test failures.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void CleanupManagedProcesses(string repoRoot)
    {
        StopProcessesByExecutablePath(
            APP_HOST_PROCESS_NAME,
            Path.Combine(repoRoot, APP_HOST_EXECUTABLE_RELATIVE_PATH));
        StopProcessesByExecutablePath(
            API_PROCESS_NAME,
            Path.Combine(repoRoot, API_EXECUTABLE_RELATIVE_PATH));
        StopProcessesListeningOnManagedPorts();
    }

    private static void StopProcessesByExecutablePath(string processName, string executablePath)
    {
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.HasExited)
                {
                    continue;
                }

                string? processPath = process.MainModule?.FileName;
                if (!PathsReferToSameFile(processPath, executablePath))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)_portShutdownTimeout.TotalMilliseconds);
            }
            catch
            {
                // Ignore race conditions and already-exited processes during cleanup.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void WaitForBuildOutputsUnlocked(string repoRoot)
    {
        WaitForFileUnlocked(Path.Combine(repoRoot, APP_HOST_EXECUTABLE_RELATIVE_PATH));
        WaitForFileUnlocked(Path.Combine(repoRoot, API_EXECUTABLE_RELATIVE_PATH));
    }

    private static void StopProcessesListeningOnManagedPorts()
    {
        foreach (int managedPort in GetManagedPorts())
        {
            StopProcessesListeningOnPort(managedPort);
        }
    }

    private static void StopProcessesListeningOnPort(int port)
    {
        foreach (int processId in GetListeningProcessIds(port))
        {
            if (processId == Environment.ProcessId)
            {
                continue;
            }

            try
            {
                using Process process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit((int)_portShutdownTimeout.TotalMilliseconds);
                }
            }
            catch
            {
                // Ignore race conditions and already-exited processes during cleanup.
            }
        }
    }

    private static IReadOnlyCollection<int> GetListeningProcessIds(int port)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetListeningProcessIdsFromWindowsNetstat(port);
        }

        return GetListeningProcessIdsFromLsof(port);
    }

    private static IReadOnlyCollection<int> GetListeningProcessIdsFromWindowsNetstat(int port)
    {
        string output = RunProcessAndCaptureOutput("netstat", "-ano -p tcp");
        var processIds = new HashSet<int>();

        foreach (string line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5
                || !string.Equals(parts[0], "TCP", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(parts[3], "LISTENING", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryParsePort(parts[1], out int parsedPort) || parsedPort != port)
            {
                continue;
            }

            if (int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out int processId))
            {
                processIds.Add(processId);
            }
        }

        return processIds;
    }

    private static IReadOnlyCollection<int> GetListeningProcessIdsFromLsof(int port)
    {
        ProcessExecutionResult result = RunProcess("lsof", $"-t -iTCP:{port.ToString(CultureInfo.InvariantCulture)} -sTCP:LISTEN");
        if (!result.Started)
        {
            throw new InvalidOperationException(
                $"Failed to determine which process is listening on port {port.ToString(CultureInfo.InvariantCulture)} because the 'lsof' utility is unavailable. Install 'lsof' and ensure it is on the PATH before running QA automation on this machine.");
        }

        if (result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.StandardError))
        {
            throw new InvalidOperationException(
                $"Failed to determine which process is listening on port {port.ToString(CultureInfo.InvariantCulture)} because 'lsof' returned exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {result.StandardError.Trim()}");
        }

        string output = result.StandardOutput;
        var processIds = new HashSet<int>();

        foreach (string line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out int processId))
            {
                processIds.Add(processId);
            }
        }

        return processIds;
    }

    private static bool TryParsePort(string endpoint, out int port)
    {
        port = default;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        int separatorIndex = endpoint.LastIndexOf(':');
        if (separatorIndex < 0 || separatorIndex == endpoint.Length - 1)
        {
            return false;
        }

        return int.TryParse(
            endpoint[(separatorIndex + 1)..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out port);
    }

    internal static bool StartupLogContainsFatalError(string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        // The API can emit handled startup exceptions to stderr before recovering
        // and reaching readiness, so stderr alone is not a reliable failure signal.

        return _fatalStartupLogMarkers.Any(marker => content.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetStartupFailureDetails(SystemUptimeTrackerAppHostReadinessScope readinessScope, out string? details)
    {
        string? appHostTempPath;

        lock (_syncLock)
        {
            appHostTempPath = _appHostTempPath;
        }

        if (string.IsNullOrWhiteSpace(appHostTempPath) || !Directory.Exists(appHostTempPath))
        {
            details = null;
            return false;
        }

        if (TryGetStartupFailureDetailsForPatterns(appHostTempPath, _apiStartupLogPatterns, out details))
        {
            return true;
        }

        if (RequiresClientReadiness(readinessScope)
            && TryGetStartupFailureDetailsForPatterns(appHostTempPath, _clientStartupLogPatterns, out details))
        {
            return true;
        }

        details = null;
        return false;
    }

    private static bool TryGetStartupFailureDetailsForPatterns(string appHostTempPath, string[] patterns, out string? details)
    {
        foreach (string pattern in patterns)
        {
            IEnumerable<string> matchingFiles;

            try
            {
                matchingFiles = Directory.EnumerateFiles(
                    appHostTempPath,
                    pattern,
                    SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (string filePath in matchingFiles
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName))
            {
                string? fileContent = TryReadLogTail(filePath, maxLines: 60);
                if (!StartupLogContainsFatalError(Path.GetFileName(filePath), fileContent ?? string.Empty))
                {
                    continue;
                }

                details = $"Startup log '{filePath}' reported a fatal error:{Environment.NewLine}{fileContent}";
                return true;
            }
        }

        details = null;
        return false;
    }

    private static string? TryReadLogTail(string filePath, int maxLines)
    {
        try
        {
            string[] allLines = File.ReadAllLines(filePath);
            if (allLines.Length == 0)
            {
                return null;
            }

            string[] tailLines = allLines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(maxLines)
                .ToArray();

            return tailLines.Length == 0
                ? null
                : string.Join(Environment.NewLine, tailLines);
        }
        catch
        {
            return null;
        }
    }

    private static string RunProcessAndCaptureOutput(string fileName, string arguments)
    {
        ProcessExecutionResult result = RunProcess(fileName, arguments);
        return result.ExitCode == 0 ? result.StandardOutput : result.StandardError;
    }

    private static ProcessExecutionResult RunProcess(string fileName, string arguments, string? workingDirectory = null)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory
                }
            };

            process.Start();
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessExecutionResult(true, process.ExitCode, standardOutput, standardError);
        }
        catch (Exception ex)
        {
            return new ProcessExecutionResult(false, -1, string.Empty, ex.Message);
        }
    }

    private static void WaitForManagedPortsToClose()
    {
        DateTime deadline = DateTime.UtcNow.Add(_portShutdownTimeout);
        while (DateTime.UtcNow < deadline)
        {
            bool portsStillListening = GetManagedPorts().Any(port => GetListeningProcessIds(port).Count > 0);
            if (!portsStillListening)
            {
                return;
            }

            StopProcessesListeningOnManagedPorts();
            Thread.Sleep(250);
        }
    }

    private static void WaitForFileUnlocked(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        DateTime deadline = DateTime.UtcNow.Add(_portShutdownTimeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using FileStream _ = new(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch
            {
                return;
            }
        }
    }

    private static void CleanupTemporaryAppHostPath(
        string? appHostTempPath,
        int maxAttempts = 10,
        int retryDelayMilliseconds = 500)
    {
        if (string.IsNullOrWhiteSpace(appHostTempPath))
        {
            return;
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                if (Directory.Exists(appHostTempPath))
                {
                    Directory.Delete(appHostTempPath, recursive: true);
                }

                return;
            }
            catch
            {
                if (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(retryDelayMilliseconds);
                }
            }
        }
    }

    private static void CleanupStaleAppHostTempDirectories(string repoRoot)
    {
        try
        {
            string appHostTempRoot = Path.Combine(repoRoot, ".tmp", "apphost");
            if (!Directory.Exists(appHostTempRoot))
            {
                return;
            }

            DateTime utcNow = DateTime.UtcNow;

            foreach (string directory in Directory.GetDirectories(appHostTempRoot)
                .Where(directory => ShouldCleanupStaleAppHostTempDirectory(directory, utcNow))
                .OrderBy(Directory.GetLastWriteTimeUtc)
                .Take(STALE_DIRECTORY_CLEANUP_MAX_COUNT))
            {
                CleanupTemporaryAppHostPath(
                    directory,
                    maxAttempts: STALE_DIRECTORY_CLEANUP_MAX_ATTEMPTS,
                    retryDelayMilliseconds: STALE_DIRECTORY_CLEANUP_RETRY_DELAY_MILLISECONDS);
            }
        }
        catch
        {
            // Ignore errors during stale directory cleanup
        }
    }

    internal static bool ShouldCleanupStaleAppHostTempDirectory(string directoryPath, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return false;
        }

        int? ownerProcessId = TryReadAppHostTempOwnerProcessId(directoryPath);
        return ShouldCleanupStaleAppHostTempDirectory(
            Directory.GetLastWriteTimeUtc(directoryPath),
            utcNow,
            ownerProcessId,
            IsProcessAlive);
    }

    internal static bool ShouldCleanupStaleAppHostTempDirectory(
        DateTime lastWriteTimeUtc,
        DateTime utcNow,
        int? ownerProcessId,
        Func<int, bool> isProcessAlive)
    {
        ArgumentNullException.ThrowIfNull(isProcessAlive);

        if (ownerProcessId is int liveOwnerProcessId && isProcessAlive(liveOwnerProcessId))
        {
            return false;
        }

        return utcNow - lastWriteTimeUtc >= _staleAppHostTempDirectoryAgeThreshold;
    }

    private static void WriteAppHostTempOwnerMarker(string appHostTempPath, int ownerProcessId)
    {
        string ownerMarkerPath = Path.Combine(appHostTempPath, APP_HOST_TEMP_OWNER_FILE_NAME);
        File.WriteAllText(ownerMarkerPath, ownerProcessId.ToString(CultureInfo.InvariantCulture));
    }

    private static int? TryReadAppHostTempOwnerProcessId(string directoryPath)
    {
        string ownerMarkerPath = Path.Combine(directoryPath, APP_HOST_TEMP_OWNER_FILE_NAME);
        if (!File.Exists(ownerMarkerPath))
        {
            return null;
        }

        try
        {
            string ownerProcessIdText = File.ReadAllText(ownerMarkerPath).Trim();
            return int.TryParse(ownerProcessIdText, NumberStyles.None, CultureInfo.InvariantCulture, out int ownerProcessId)
                ? ownerProcessId
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
