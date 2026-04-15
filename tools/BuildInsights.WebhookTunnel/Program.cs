// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Azure.Identity;
using BuildInsights.ServiceDefaults;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Services.Common;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

await WebhookTunnelCommand.RunAsync();

/// <summary>
/// Starts a dev tunnel to the local Build Insights API and reconfigures the shared GitHub App
/// and Azure DevOps service hooks to point to the tunnel while it's running.
/// The devtunnel can be started from the Aspire dashboard or just manually by running this project.
/// </summary>
internal static class WebhookTunnelCommand
{
    private const string GitHubWebhookPath = "/api/github/webhooks";
    private const string EnabledSubscriptionStatus = "enabled";

    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.AddSharedConfiguration();
        builder.Services.AddSingleton<TunnelReadinessState>();

        var keyVaultName = builder.Configuration.GetRequiredValue(BuildInsightsCommonConfiguration.ConfigurationKeys.KeyVaultName);
        var credential = new DefaultAzureCredential();
        builder.Configuration.AddAzureKeyVault(
            new Uri($"https://{keyVaultName}.vault.azure.net/"),
            credential,
            new KeyVaultSecretsWithPrefix(BuildInsightsCommonConfiguration.ConfigurationKeys.KeyVaultSecretPrefix));

        var gitHubAppId = builder.Configuration.GetValue<int?>(BuildInsightsCommonConfiguration.ConfigurationKeys.GitHubApp + ":AppId")
            ?? throw new InvalidOperationException("GitHubApp:AppId is not configured.");
        var gitHubAppPrivateKey = builder.Configuration[BuildInsightsCommonConfiguration.ConfigurationKeys.GitHubAppPrivateKey];

        builder.Services.AddSingleton<IGitHubClientFactory, GitHubClientFactory>();
        builder.Services.Configure<GitHubTokenProviderOptions>(o =>
        {
            o.GitHubAppId = gitHubAppId;
            o.PrivateKey = gitHubAppPrivateKey;
        });
        builder.Services.AddGitHubTokenProvider();

        using var cancellationTokenSource = new CancellationTokenSource();
        await using var app = builder.Build();
        app.MapGet("/health", (TunnelReadinessState readinessState) =>
            readinessState.IsReady
                ? Results.Ok(new { status = "Healthy", phase = readinessState.Phase })
                : Results.Json(
                    new { status = "Starting", phase = readinessState.Phase },
                    statusCode: StatusCodes.Status503ServiceUnavailable));

        await app.StartAsync(cancellationTokenSource.Token);

        var options = TunnelCommandOptions.FromConfiguration(builder.Configuration);
        await using var runner = ActivatorUtilities.CreateInstance<WebhookTunnelRunner>(app.Services, options);

        void RequestShutdown()
        {
            runner.RequestShutdown();

            if (!cancellationTokenSource.IsCancellationRequested)
            {
                cancellationTokenSource.Cancel();
            }
        }

        void consoleCancelHandler(object? _, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            RequestShutdown();
        }
        void processExitHandler(object? _, EventArgs __) => RequestShutdown();
        void unloadingHandler(AssemblyLoadContext _) => RequestShutdown();

        using var applicationStoppingRegistration = app.Services
            .GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping
            .Register(RequestShutdown);

        Console.CancelKeyPress += consoleCancelHandler;
        AppDomain.CurrentDomain.ProcessExit += processExitHandler;
        AssemblyLoadContext.Default.Unloading += unloadingHandler;

        try
        {
            await runner.RunAsync(cancellationTokenSource.Token);
        }
        finally
        {
            Console.CancelKeyPress -= consoleCancelHandler;
            AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
            AssemblyLoadContext.Default.Unloading -= unloadingHandler;
            await app.StopAsync(CancellationToken.None);
        }
    }

    private sealed class WebhookTunnelRunner : IAsyncDisposable
    {
        private readonly TunnelCommandOptions _options;
        private readonly IGitHubAppTokenProvider _gitHubAppTokenProvider;
        private readonly TunnelReadinessState _readinessState;
        private readonly ProcessManager _processManager;
        private readonly HttpClient _healthCheckClient;
        private readonly StringBuilder _devTunnelLog = new();

        private Process? _devTunnelProcess;
        private string? _tunnelUrl;
        private bool _cleanedUp;

        public WebhookTunnelRunner(
            TunnelCommandOptions options,
            IGitHubAppTokenProvider gitHubAppTokenProvider,
            TunnelReadinessState readinessState)
        {
            _options = options;
            _gitHubAppTokenProvider = gitHubAppTokenProvider;
            _readinessState = readinessState;
            _processManager = new ProcessManager(NullLogger.Instance, gitExecutable: "git");
            _healthCheckClient = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        public void RequestShutdown()
        {
            TryStopDevTunnelProcess();
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                _readinessState.ReportProgress("Starting up");

                WriteSection("Build Insights local webhook tunnel");
                Console.WriteLine("ℹ️ This workflow re-points the shared Build Insights dev webhook targets.");
                Console.WriteLine("👤 Only one developer should run it at a time.");

                await EnsureCommandAvailableAsync(
                    "devtunnel",
                    "Install the dev tunnel CLI with 'winget install Microsoft.devtunnel' and run 'devtunnel user login'.",
                    cancellationToken);

                await EnsureDevTunnelLoggedInAsync(cancellationToken);

                _readinessState.ReportProgress("Checking local API health");
                WriteSection("Checking local service health");
                await WaitForHealthyUrlAsync($"{_options.LocalBaseUrl}/health", cancellationToken);
                Console.WriteLine($"✅ Local API is healthy at {_options.LocalBaseUrl}/health");

                _readinessState.ReportProgress("Creating the dev tunnel");
                WriteSection("Starting the dev tunnel");
                var tunnelInfo = await StartDevTunnelAsync(cancellationToken);
                _tunnelUrl = tunnelInfo.TunnelUrl;

                Console.WriteLine($"🌐 Tunnel URL: {_tunnelUrl}");
                if (!string.IsNullOrWhiteSpace(tunnelInfo.InspectUrl))
                {
                    Console.WriteLine($"🔎 Inspect URL: {tunnelInfo.InspectUrl}");
                }

                await WaitForHealthyUrlAsync($"{_tunnelUrl}/health", cancellationToken);
                Console.WriteLine("✅ Public tunnel health check succeeded.");

                _readinessState.ReportProgress("Configuring the GitHub webhook");
                await ConfigureGitHubWebhookAsync(cancellationToken);
                _readinessState.ReportProgress("Configuring Azure DevOps service hooks");
                await ConfigureAzureDevOpsHooksAsync(cancellationToken);

                _readinessState.ReportReady("Tunnel and webhooks are ready");
                Console.WriteLine("✅ Tunnel and webhooks are ready");
                Console.WriteLine($"🐙 GitHub endpoint: {_tunnelUrl}{GitHubWebhookPath}");

                Console.WriteLine();
                Console.WriteLine("🛑 Press Ctrl+C or stop the Aspire dashboard resource to restore the shared webhook configuration.");

                await WaitForShutdownAsync(cancellationToken);
            }
            finally
            {
                await CleanupAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await CleanupAsync();
            _healthCheckClient.Dispose();
        }

        private async Task ConfigureGitHubWebhookAsync(CancellationToken cancellationToken)
        {
            WriteSection("Configuring the GitHub App webhook");

            var originalWebhook = await GetGitHubWebhookConfigAsync(cancellationToken);
            Console.WriteLine($"Current GitHub webhook URL: {originalWebhook.Url}");

            var newConfig = new GitHubWebhookConfig(
                originalWebhook.ContentType ?? "json",
                originalWebhook.InsecureSsl ?? "0",
                $"{_tunnelUrl}{GitHubWebhookPath}");

            await UpdateGitHubWebhookConfigAsync(newConfig, cancellationToken);
            Console.WriteLine($"GitHub webhook URL updated to {newConfig.Url}");
        }

        private async Task ConfigureAzureDevOpsHooksAsync(CancellationToken cancellationToken)
        {
            WriteSection("Configuring Azure DevOps service hooks");

            using var azureDevOpsHttpClient = await CreateAzureDevOpsHttpClientAsync();

            Console.WriteLine("🔎 Searching for existing dev tunnel service hook subscriptions...");

            using var listResponse = await azureDevOpsHttpClient.GetAsync(
                $"https://dev.azure.com/{_options.AzDoOrganization}/_apis/hooks/subscriptions?api-version=7.1",
                cancellationToken);

            listResponse.EnsureSuccessStatusCode();

            var json = await listResponse.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("Azure DevOps did not return a service hook list.");

            var subscriptions = json["value"]?.AsArray()
                ?? throw new InvalidOperationException("Azure DevOps service hook list is missing the 'value' property.");

            var updated = 0;

            foreach (var subscription in subscriptions)
            {
                var url = subscription?["consumerInputs"]?["url"]?.GetValue<string>();
                if (url is null || !url.Contains(".devtunnels.ms", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var subscriptionId = subscription!["id"]!.GetValue<string>();
                var originalUri = new Uri(url);
                var newUrl = $"{_tunnelUrl}{originalUri.PathAndQuery}";
                var previousStatus = subscription["status"]?.GetValue<string>();

                subscription["consumerInputs"]!["url"] = newUrl;
                subscription["status"] = EnabledSubscriptionStatus;

                using var putResponse = await HttpClientJsonExtensions.PutAsJsonAsync(
                    azureDevOpsHttpClient,
                    $"https://dev.azure.com/{_options.AzDoOrganization}/_apis/hooks/subscriptions/{subscriptionId}?api-version=7.1",
                    subscription,
                    cancellationToken);

                if (!putResponse.IsSuccessStatusCode)
                {
                    var body = await putResponse.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"  ⚠️ Failed to update service hook {subscriptionId}: {(int)putResponse.StatusCode} {body}");
                    continue;
                }

                updated++;
            }

            if (updated == 0)
            {
                Console.WriteLine("⚠️ No existing dev tunnel service hooks found to update.");
            }
            else
            {
                Console.WriteLine($"✅ Updated {updated} dev tunnel service hook(s).");
            }
        }

        private Task CleanupAsync()
        {
            if (_cleanedUp)
            {
                return Task.CompletedTask;
            }

            _cleanedUp = true;
            _readinessState.ReportProgress("Cleaning up");

            WriteSection("Cleaning up");
            TryStopDevTunnelProcess();
            _devTunnelProcess?.Dispose();
            _devTunnelProcess = null;

            return Task.CompletedTask;
        }

        private async Task EnsureDevTunnelLoggedInAsync(CancellationToken cancellationToken)
        {
            var result = await _processManager.Execute("devtunnel", ["user", "show"], cancellationToken: cancellationToken);
            var combinedOutput = $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";

            if (result.ExitCode != 0 ||
                combinedOutput.Contains("not logged in", StringComparison.OrdinalIgnoreCase) ||
                combinedOutput.Contains("sign in", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("devtunnel is not logged in. Run 'devtunnel user login' first.");
            }
        }

        private async Task<TunnelInfo> StartDevTunnelAsync(CancellationToken cancellationToken)
        {
            var tunnelInfoSource = new TaskCompletionSource<TunnelInfo>(TaskCreationOptions.RunContinuationsAsynchronously);

            var startInfo = new ProcessStartInfo
            {
                FileName = "devtunnel",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            startInfo.ArgumentList.Add("host");
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(_options.Port.ToString());
            startInfo.ArgumentList.Add("--protocol");
            startInfo.ArgumentList.Add("https");
            startInfo.ArgumentList.Add("--allow-anonymous");

            _devTunnelProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };

            string? tunnelUrl = null;
            string? inspectUrl = null;
            string? tunnelId = null;

            _devTunnelProcess.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is null)
                {
                    return;
                }

                lock (_devTunnelLog)
                {
                    _devTunnelLog.AppendLine(eventArgs.Data);
                }

                Console.WriteLine(eventArgs.Data);

                tunnelUrl ??= ParseOutputValue(eventArgs.Data, @"Connect via browser: (?<value>https://[^\s,]+)");
                inspectUrl ??= ParseOutputValue(eventArgs.Data, @"Inspect network activity: (?<value>https://[^\s,]+)");
                tunnelId ??= ParseOutputValue(eventArgs.Data, @"Ready to accept connections for tunnel: (?<value>[a-z0-9\-]+)");

                if (tunnelUrl is not null)
                {
                    tunnelInfoSource.TrySetResult(new TunnelInfo(tunnelUrl.TrimEnd('/'), inspectUrl?.TrimEnd('/'), tunnelId));
                }
            };

            _devTunnelProcess.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is null)
                {
                    return;
                }

                lock (_devTunnelLog)
                {
                    _devTunnelLog.AppendLine(eventArgs.Data);
                }

                Console.Error.WriteLine(eventArgs.Data);
            };

            _devTunnelProcess.Exited += (_, _) =>
            {
                if (tunnelInfoSource.Task.IsCompleted)
                {
                    return;
                }

                tunnelInfoSource.TrySetException(new InvalidOperationException(
                    $"devtunnel exited before it published a public URL.{Environment.NewLine}{_devTunnelLog}"));
            };

            if (!_devTunnelProcess.Start())
            {
                throw new InvalidOperationException("Failed to start the devtunnel host process.");
            }

            _devTunnelProcess.BeginOutputReadLine();
            _devTunnelProcess.BeginErrorReadLine();

            var completedTask = await Task.WhenAny(
                tunnelInfoSource.Task,
                Task.Delay(TimeSpan.FromSeconds(45), cancellationToken));

            if (completedTask != tunnelInfoSource.Task)
            {
                throw new TimeoutException($"Timed out waiting for devtunnel to publish a public URL.{Environment.NewLine}{_devTunnelLog}");
            }

            return await tunnelInfoSource.Task;
        }

        private async Task WaitForHealthyUrlAsync(string url, CancellationToken cancellationToken)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
            Exception? lastException = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    using var response = await _healthCheckClient.GetAsync(url, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastException = ex;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }

            throw new TimeoutException($"Timed out waiting for '{url}' to become healthy.{Environment.NewLine}{lastException?.Message}");
        }

        private async Task WaitForShutdownAsync(CancellationToken cancellationToken)
        {
            if (_devTunnelProcess is null)
            {
                return;
            }

            using var cancellationRegistration = cancellationToken.Register(TryStopDevTunnelProcess);
            await _devTunnelProcess.WaitForExitAsync(CancellationToken.None);

            if (!cancellationToken.IsCancellationRequested && _devTunnelProcess.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The dev tunnel host exited unexpectedly with code {_devTunnelProcess.ExitCode}.{Environment.NewLine}{_devTunnelLog}");
            }
        }

        private void TryStopDevTunnelProcess()
        {
            if (_devTunnelProcess is { HasExited: false })
            {
                try
                {
                    Console.WriteLine("🛑 Stopping the dev tunnel host process...");
                    _devTunnelProcess.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to stop the dev tunnel host process: {ex.Message}");
                }
            }
        }

        private async Task<HttpClient> CreateAzureDevOpsHttpClientAsync()
        {
            var tokenProviderOptions = new AzureDevOpsTokenProviderOptions
            {
                [_options.AzDoOrganization] = new AzureDevOpsCredentialResolverOptions(),
            };

            var tokenProvider = AzureDevOpsTokenProvider.FromStaticOptions(tokenProviderOptions);
            var accessToken = await tokenProvider.GetTokenForAccountAsync(_options.AzDoOrganization);
            var basicAuthValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{accessToken}"));

            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuthValue);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }

        private async Task<GitHubWebhookConfig> GetGitHubWebhookConfigAsync(CancellationToken cancellationToken)
        {
            using var httpClient = CreateGitHubHttpClient();
            using var response = await httpClient.GetAsync("https://api.github.com/app/hook/config", cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<GitHubWebhookConfig>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("GitHub did not return the current webhook configuration.");
        }

        private async Task UpdateGitHubWebhookConfigAsync(GitHubWebhookConfig config, CancellationToken cancellationToken)
        {
            using var httpClient = CreateGitHubHttpClient();
            using var response = await httpClient.PatchAsJsonAsync(
                "https://api.github.com/app/hook/config",
                new GitHubWebhookPatchRequest(config.ContentType ?? "json", config.InsecureSsl ?? "0", config.Url ?? string.Empty),
                cancellationToken);

            response.EnsureSuccessStatusCode();
        }

        private HttpClient CreateGitHubHttpClient()
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _gitHubAppTokenProvider.GetAppToken());
            httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BuildInsights", "dev-tunnel"));

            return httpClient;
        }

        private static string? ParseOutputValue(string input, string pattern)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
        }

        private async Task EnsureCommandAvailableAsync(string commandName, string installHint, CancellationToken cancellationToken)
        {
            try
            {
                await _processManager.Execute(commandName, ["--version"], timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                throw new InvalidOperationException($"{commandName} was not found. {installHint}", ex);
            }
        }

        private static void WriteSection(string message)
        {
            Console.WriteLine();
            Console.WriteLine($"{GetSectionEmoji(message)} {message}");
        }

        private static string GetSectionEmoji(string message) => message switch
        {
            "Build Insights local webhook tunnel" => "🚇",
            "Checking local service health" => "🩺",
            "Starting the dev tunnel" => "🚀",
            "Configuring the GitHub App webhook" => "🐙",
            "Configuring Azure DevOps service hooks" => "🛠️",
            "Cleaning up" => "🧹",
            _ => "📌",
        };
    }

    private sealed class TunnelReadinessState
    {
        private readonly object _lock = new();
        private string _phase = "Starting up";
        private bool _isReady;

        public string Phase
        {
            get
            {
                lock (_lock)
                {
                    return _phase;
                }
            }
        }

        public bool IsReady
        {
            get
            {
                lock (_lock)
                {
                    return _isReady;
                }
            }
        }

        public void ReportProgress(string phase)
        {
            lock (_lock)
            {
                _phase = phase;
                _isReady = false;
            }
        }

        public void ReportReady(string phase)
        {
            lock (_lock)
            {
                _phase = phase;
                _isReady = true;
            }
        }
    }

    private sealed record TunnelCommandOptions(
        int Port,
        string LocalBaseUrl,
        string AzDoOrganization,
        string AzDoProject,
        string KeyVaultName)
    {
        public static TunnelCommandOptions FromConfiguration(IConfiguration configuration)
        {
            var port = configuration.GetValue<int?>("BUILD_INSIGHTS_API_PORT")
                ?? configuration.GetValue<int?>("BuildInsightsApiHttpsPort")
                ?? throw new InvalidOperationException("BUILD_INSIGHTS_API_PORT or BuildInsightsApiHttpsPort is not configured.");
            var localBaseUrl = configuration.GetValue<string>("BUILD_INSIGHTS_API_BASE_URL")
                ?? $"https://localhost:{port}";
            var keyVaultName = configuration.GetValue<string>(BuildInsightsCommonConfiguration.ConfigurationKeys.KeyVaultName)
                ?? throw new InvalidOperationException($"{BuildInsightsCommonConfiguration.ConfigurationKeys.KeyVaultName} is not configured.");
            var azDoOrganization = configuration.GetValue<string>("WebhookTunnel:AzDoOrganization")
                ?? throw new InvalidOperationException("WebhookTunnel:AzDoOrganization is not configured.");
            var azDoProject = configuration.GetValue<string>("WebhookTunnel:AzDoProject")
                ?? throw new InvalidOperationException("WebhookTunnel:AzDoProject is not configured.");

            return new TunnelCommandOptions(port, localBaseUrl.TrimEnd('/'), azDoOrganization, azDoProject, keyVaultName);
        }
    }

    private sealed record TunnelInfo(string TunnelUrl, string? InspectUrl, string? TunnelId);

    private sealed record GitHubWebhookConfig(
        [property: JsonPropertyName("content_type")] string? ContentType,
        [property: JsonPropertyName("insecure_ssl")] string? InsecureSsl,
        [property: JsonPropertyName("url")] string? Url);

    private sealed record GitHubWebhookPatchRequest(
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("insecure_ssl")] string InsecureSsl,
        [property: JsonPropertyName("url")] string Url);
}
