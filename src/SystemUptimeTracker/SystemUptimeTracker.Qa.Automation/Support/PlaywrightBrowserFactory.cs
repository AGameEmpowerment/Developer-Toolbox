using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Diagnostics;

namespace SystemUptimeTracker.Qa.Automation.Support
{
    internal sealed class PlaywrightBrowserFactory : IPlaywrightBrowserFactory
    {
        private static readonly SemaphoreSlim _browserInstallLock = new(1, 1);
        private static bool _browserInstallEnsured;

        private readonly IPlaywrightBrowserEnvironment _environment;
        private readonly ILogger<PlaywrightBrowserFactory> _logger;
        private IPlaywright? _playwright;
        private IBrowser? _browser;

        public PlaywrightBrowserFactory(
            IPlaywrightBrowserEnvironment environment,
            ILogger<PlaywrightBrowserFactory> logger)
        {
            _environment = environment;
            _logger = logger;
        }

        public async Task<IBrowser> GetBrowserAsync()
        {
            if (_browser is not null)
            {
                return _browser;
            }

            WebBrowserConfiguration configuration = _environment.BrowserConfiguration;
            _playwright = await Playwright.CreateAsync();
            BrowserTypeLaunchOptions launchOptions = new()
            {
                Headless = configuration.HeadlessBrowser,
                Timeout = configuration.TimeoutCommandSecs * 1000
            };

            (string browserName, IBrowserType browserType) = configuration.BrowserType.ToUpperInvariant() switch
            {
                "FIREFOX" => ("firefox", _playwright.Firefox),
                "WEBKIT" => ("webkit", _playwright.Webkit),
                _ => ("chromium", _playwright.Chromium)
            };

            await EnsureBrowserInstalledAsync(browserType, browserName).ConfigureAwait(false);

            _browser = await browserType.LaunchAsync(launchOptions).ConfigureAwait(false);

            _logger.LogInformation(
                "Started Playwright {BrowserType} browser. Headless={HeadlessBrowser}.",
                configuration.BrowserType,
                configuration.HeadlessBrowser);

            return _browser;
        }

        public async ValueTask DisposeAsync()
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
            }

            _playwright?.Dispose();
        }

        private async Task EnsureBrowserInstalledAsync(IBrowserType browserType, string browserName)
        {
            string executablePath = browserType.ExecutablePath;
            if (_browserInstallEnsured || File.Exists(executablePath))
            {
                _browserInstallEnsured = true;
                return;
            }

            await _browserInstallLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_browserInstallEnsured || File.Exists(executablePath))
                {
                    _browserInstallEnsured = true;
                    return;
                }

                string scriptPath = Path.Combine(AppContext.BaseDirectory, "playwright.ps1");
                if (!File.Exists(scriptPath))
                {
                    throw new FileNotFoundException("The Playwright install script was not found in the QA automation output directory.", scriptPath);
                }

                string shellCommand = OperatingSystem.IsWindows() ? "pwsh" : "pwsh";
                if (OperatingSystem.IsWindows() && !CommandExists(shellCommand))
                {
                    shellCommand = "powershell";
                }

                var startInfo = new ProcessStartInfo(shellCommand)
                {
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" install {browserName}",
                    WorkingDirectory = AppContext.BaseDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _logger.LogInformation("Installing Playwright browser {BrowserName} because executable {ExecutablePath} is missing.", browserName, executablePath);

                using Process process = Process.Start(startInfo)
                                        ?? throw new InvalidOperationException($"Unable to start Playwright install script using {shellCommand}.");

                string standardOutput = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                string standardError = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                await process.WaitForExitAsync().ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Playwright browser installation failed with exit code {process.ExitCode}. Output: {standardOutput} Error: {standardError}");
                }

                if (!File.Exists(executablePath))
                {
                    throw new FileNotFoundException(
                        $"Playwright reported a successful install for {browserName}, but the browser executable is still missing.",
                        executablePath);
                }

                _browserInstallEnsured = true;
                _logger.LogInformation("Installed Playwright browser {BrowserName} successfully.", browserName);
            }
            finally
            {
                _browserInstallLock.Release();
            }
        }

        private static bool CommandExists(string command)
        {
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string path in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(path, OperatingSystem.IsWindows() ? $"{command}.exe" : command);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}