// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Maestro.Common.AzureDevOpsTokens;

internal static class WebhookTunnelCommand
{
    private const string CommandName = "webhook-tunnel";
    private const string GitHubWebhookPath = "/api/github/webhooks";

    private static readonly HookDefinition[] HookDefinitions =
    [
        new(
            "BuildCompleted",
            "tfs",
            "build.complete",
            "2.0",
            "/api/azdo/servicehooks/build.complete",
            projectId => new Dictionary<string, string>
            {
                ["projectId"] = projectId,
            }),
        new(
            "PipelineRunStateChanged",
            "pipelines",
            "ms.vss-pipelines.run-state-changed-event",
            "5.1-preview.1",
            "/api/azdo/servicehooks/ms.vss-pipelines.run-state-changed-event",
            projectId => new Dictionary<string, string>
            {
                ["projectId"] = projectId,
                ["runStateId"] = "Completed",
            }),
        new(
            "PipelineStageStateChanged",
            "pipelines",
            "ms.vss-pipelines.stage-state-changed-event",
            "5.1-preview.1",
            "/api/azdo/servicehooks/ms.vss-pipelines.stage-state-changed-event",
            projectId => new Dictionary<string, string>
            {
                ["projectId"] = projectId,
                ["stageStateId"] = "Completed",
            }),
    ];

    public static bool ShouldRun(string[] args)
        => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static async Task RunAsync(string[] args)
    {
        var options = TunnelCommandOptions.Parse(args);
        using var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        await using var runner = new WebhookTunnelRunner(options);
        await runner.RunAsync(cancellationTokenSource.Token);
    }

    private sealed class WebhookTunnelRunner : IAsyncDisposable
    {
        private readonly TunnelCommandOptions _options;
        private readonly SecretClient _secretClient;
        private readonly HttpClient _healthCheckClient;
        private readonly StringBuilder _devTunnelLog = new();
        private readonly List<string> _azDoSubscriptionIds = [];

        private Process? _devTunnelProcess;
        private GitHubWebhookConfig? _originalGitHubWebhookConfig;
        private string? _gitHubPrivateKeyPem;
        private string? _tunnelUrl;
        private bool _cleanedUp;

        public WebhookTunnelRunner(TunnelCommandOptions options)
        {
            _options = options;

            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = false,
            });

            _secretClient = new SecretClient(new Uri($"https://{options.KeyVaultName}.vault.azure.net/"), credential);
            _healthCheckClient = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                WriteSection("Build Insights local webhook tunnel");
                Console.WriteLine("This workflow re-points the shared Build Insights dev webhook targets.");
                Console.WriteLine("Only one developer should run it at a time.");

                EnsureCommandAvailable(
                    "devtunnel",
                    "Install the dev tunnel CLI with 'winget install Microsoft.devtunnel' and run 'devtunnel user login'.");

                await EnsureDevTunnelLoggedInAsync(cancellationToken);

                WriteSection("Checking local service health");
                await WaitForHealthyUrlAsync($"https://localhost:{_options.Port}/health", cancellationToken);
                Console.WriteLine($"Local API is healthy at https://localhost:{_options.Port}/health");

                WriteSection("Starting the dev tunnel");
                var tunnelInfo = await StartDevTunnelAsync(cancellationToken);
                _tunnelUrl = tunnelInfo.TunnelUrl;

                Console.WriteLine($"Tunnel URL: {_tunnelUrl}");
                if (!string.IsNullOrWhiteSpace(tunnelInfo.InspectUrl))
                {
                    Console.WriteLine($"Inspect URL: {tunnelInfo.InspectUrl}");
                }

                await WaitForHealthyUrlAsync($"{_tunnelUrl}/health", cancellationToken);
                Console.WriteLine("Public tunnel health check succeeded.");

                if (!_options.SkipGitHub)
                {
                    await ConfigureGitHubWebhookAsync(cancellationToken);
                }
                else
                {
                    Console.WriteLine("Skipping GitHub webhook configuration.");
                }

                if (!_options.SkipAzDo)
                {
                    await ConfigureAzureDevOpsHooksAsync(cancellationToken);
                }
                else
                {
                    Console.WriteLine("Skipping Azure DevOps service hook configuration.");
                }

                WriteSection("Ready");
                Console.WriteLine($"GitHub endpoint: {_tunnelUrl}{GitHubWebhookPath}");
                foreach (var hookDefinition in HookDefinitions)
                {
                    Console.WriteLine($"Azure DevOps endpoint: {_tunnelUrl}{hookDefinition.RelativePath}");
                }

                Console.WriteLine();
                Console.WriteLine("Press Ctrl+C or stop the Aspire dashboard resource to restore the shared webhook configuration.");

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
            WriteSection("Updating the GitHub App webhook");

            _gitHubPrivateKeyPem = await GetSecretValueAsync(
                "BUILD_INSIGHTS_GITHUB_APP_PRIVATE_KEY",
                "github-app-private-key",
                cancellationToken);

            _originalGitHubWebhookConfig = await GetGitHubWebhookConfigAsync(cancellationToken);
            Console.WriteLine($"Current GitHub webhook URL: {_originalGitHubWebhookConfig.Url}");

            var newConfig = new GitHubWebhookConfig(
                _originalGitHubWebhookConfig.ContentType ?? "json",
                _originalGitHubWebhookConfig.InsecureSsl ?? "0",
                $"{_tunnelUrl}{GitHubWebhookPath}");

            await UpdateGitHubWebhookConfigAsync(newConfig, cancellationToken);
            Console.WriteLine($"GitHub webhook URL updated to {newConfig.Url}");
        }

        private async Task ConfigureAzureDevOpsHooksAsync(CancellationToken cancellationToken)
        {
            WriteSection("Creating Azure DevOps service hook subscriptions");

            var azDoSecretHeaderValue = await GetSecretValueAsync(
                "BUILD_INSIGHTS_AZDO_SERVICE_HOOK_SECRET",
                "azdo-service-hook-secret",
                cancellationToken);

            using var azureDevOpsHttpClient = await CreateAzureDevOpsHttpClientAsync(cancellationToken);
            var projectId = await GetAzureDevOpsProjectIdAsync(azureDevOpsHttpClient, cancellationToken);
            Console.WriteLine($"Resolved Azure DevOps project '{_options.AzDoProject}' to {projectId}");

            foreach (var hookDefinition in HookDefinitions)
            {
                var payload = new
                {
                    publisherId = hookDefinition.PublisherId,
                    eventType = hookDefinition.EventType,
                    resourceVersion = hookDefinition.ResourceVersion,
                    consumerId = "webHooks",
                    consumerActionId = "httpRequest",
                    publisherInputs = hookDefinition.CreatePublisherInputs(projectId),
                    consumerInputs = new
                    {
                        url = $"{_tunnelUrl}{hookDefinition.RelativePath}",
                        httpHeaders = $"X-BuildAnalysis-Secret:{azDoSecretHeaderValue}",
                    },
                };

                using var response = await azureDevOpsHttpClient.PostAsJsonAsync(
                    $"https://dev.azure.com/{_options.AzDoOrganization}/_apis/hooks/subscriptions?api-version=7.1",
                    payload,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                var subscription = await response.Content.ReadFromJsonAsync<AzureDevOpsSubscriptionResponse>(cancellationToken: cancellationToken)
                    ?? throw new InvalidOperationException($"Azure DevOps did not return a subscription id for {hookDefinition.Name}.");

                _azDoSubscriptionIds.Add(subscription.Id);
                Console.WriteLine($"Created {hookDefinition.Name} subscription: {subscription.Id}");
            }
        }

        private async Task CleanupAsync()
        {
            if (_cleanedUp)
            {
                return;
            }

            _cleanedUp = true;

            WriteSection("Cleaning up");

            if (_azDoSubscriptionIds.Count > 0)
            {
                try
                {
                    using var azureDevOpsHttpClient = await CreateAzureDevOpsHttpClientAsync(CancellationToken.None);
                    foreach (var subscriptionId in _azDoSubscriptionIds.AsEnumerable().Reverse())
                    {
                        Console.WriteLine($"Removing Azure DevOps subscription {subscriptionId}...");
                        using var response = await azureDevOpsHttpClient.DeleteAsync(
                            $"https://dev.azure.com/{_options.AzDoOrganization}/_apis/hooks/subscriptions/{subscriptionId}?api-version=7.1",
                            CancellationToken.None);

                        if (!response.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"Azure DevOps delete returned {(int)response.StatusCode} for subscription {subscriptionId}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to remove one or more Azure DevOps subscriptions: {ex.Message}");
                }
            }

            if (_originalGitHubWebhookConfig is not null && !string.IsNullOrWhiteSpace(_gitHubPrivateKeyPem))
            {
                try
                {
                    Console.WriteLine("Restoring the GitHub App webhook URL...");
                    await UpdateGitHubWebhookConfigAsync(_originalGitHubWebhookConfig, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to restore the GitHub App webhook configuration: {ex.Message}");
                }
            }

            TryStopDevTunnelProcess();
        }

        private async Task EnsureDevTunnelLoggedInAsync(CancellationToken cancellationToken)
        {
            var result = await RunProcessForOutputAsync("devtunnel", ["user", "show"], cancellationToken);
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

                if (tunnelUrl is null)
                {
                    tunnelUrl = ParseOutputValue(eventArgs.Data, @"Hosting port \d+ at (?<value>https://[^\s,]+)");
                }

                if (inspectUrl is null)
                {
                    inspectUrl = ParseOutputValue(eventArgs.Data, @"inspect it at (?<value>https://[^\s,]+)");
                }

                if (tunnelId is null)
                {
                    tunnelId = ParseOutputValue(eventArgs.Data, @"Ready to accept connections for tunnel: (?<value>[a-z0-9\-]+)");
                }

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
                    Console.WriteLine("Stopping the dev tunnel host process...");
                    _devTunnelProcess.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to stop the dev tunnel host process: {ex.Message}");
                }
            }
        }

        private async Task<HttpClient> CreateAzureDevOpsHttpClientAsync(CancellationToken cancellationToken)
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

        private async Task<string> GetAzureDevOpsProjectIdAsync(HttpClient client, CancellationToken cancellationToken)
        {
            using var response = await client.GetAsync(
                $"https://dev.azure.com/{_options.AzDoOrganization}/_apis/projects/{Uri.EscapeDataString(_options.AzDoProject)}?api-version=7.1",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var project = await response.Content.ReadFromJsonAsync<AzureDevOpsProjectResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException($"Azure DevOps did not return the project '{_options.AzDoProject}'.");

            return project.Id;
        }

        private async Task<string> GetSecretValueAsync(string environmentVariableName, string secretName, CancellationToken cancellationToken)
        {
            var environmentValue = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue;
            }

            var secret = await _secretClient.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            return secret.Value.Value;
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
            if (string.IsNullOrWhiteSpace(_gitHubPrivateKeyPem))
            {
                throw new InvalidOperationException("The GitHub App private key has not been loaded.");
            }

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                CreateGitHubAppJwt(_options.GitHubAppId, _gitHubPrivateKeyPem));
            httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BuildInsights", "dev-tunnel"));

            return httpClient;
        }

        private static string CreateGitHubAppJwt(int gitHubAppId, string privateKeyPem)
        {
            var now = DateTimeOffset.UtcNow;
            var header = Base64UrlEncode("{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
            var payload = Base64UrlEncode($"{{\"iat\":{now.AddMinutes(-1).ToUnixTimeSeconds()},\"exp\":{now.AddMinutes(9).ToUnixTimeSeconds()},\"iss\":{gitHubAppId}}}");
            var unsignedToken = $"{header}.{payload}";

            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);

            var signatureBytes = rsa.SignData(
                Encoding.UTF8.GetBytes(unsignedToken),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            return $"{unsignedToken}.{Base64UrlEncode(signatureBytes)}";
        }

        private static string Base64UrlEncode(string value)
            => Base64UrlEncode(Encoding.UTF8.GetBytes(value));

        private static string Base64UrlEncode(byte[] value)
            => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string? ParseOutputValue(string input, string pattern)
        {
            var match = System.Text.RegularExpressions.Regex.Match(input, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value : null;
        }

        private static async Task<ProcessOutputResult> RunProcessForOutputAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start '{fileName}'.");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);

            return new ProcessOutputResult(
                process.ExitCode,
                await standardOutputTask,
                await standardErrorTask);
        }

        private static void EnsureCommandAvailable(string commandName, string installHint)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = commandName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Arguments = "--version",
                });

                if (process is null)
                {
                    throw new InvalidOperationException();
                }

                process.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                throw new InvalidOperationException($"{commandName} was not found. {installHint}", ex);
            }
        }

        private static void WriteSection(string message)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {message} ===");
        }
    }

    private sealed record TunnelCommandOptions(
        int Port,
        string AzDoOrganization,
        string AzDoProject,
        string KeyVaultName,
        int GitHubAppId,
        bool SkipGitHub,
        bool SkipAzDo)
    {
        public static TunnelCommandOptions Parse(string[] args)
        {
            var port = 53180;
            var azDoOrganization = "dnceng-public";
            var azDoProject = "public";
            var keyVaultName = "build-insights-dev";
            var gitHubAppId = 2892253;
            var skipGitHub = false;
            var skipAzDo = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port":
                        port = int.Parse(args[++i]);
                        break;
                    case "--organization":
                        azDoOrganization = args[++i];
                        break;
                    case "--project":
                        azDoProject = args[++i];
                        break;
                    case "--keyvault-name":
                        keyVaultName = args[++i];
                        break;
                    case "--github-app-id":
                        gitHubAppId = int.Parse(args[++i]);
                        break;
                    case "--skip-github":
                        skipGitHub = true;
                        break;
                    case "--skip-azdo":
                        skipAzDo = true;
                        break;
                }
            }

            return new TunnelCommandOptions(port, azDoOrganization, azDoProject, keyVaultName, gitHubAppId, skipGitHub, skipAzDo);
        }
    }

    private sealed record HookDefinition(
        string Name,
        string PublisherId,
        string EventType,
        string ResourceVersion,
        string RelativePath,
        Func<string, Dictionary<string, string>> CreatePublisherInputs);

    private sealed record TunnelInfo(string TunnelUrl, string? InspectUrl, string? TunnelId);

    private sealed record ProcessOutputResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record AzureDevOpsProjectResponse([property: JsonPropertyName("id")] string Id);

    private sealed record AzureDevOpsSubscriptionResponse([property: JsonPropertyName("id")] string Id);

    private sealed record GitHubWebhookConfig(
        [property: JsonPropertyName("content_type")] string? ContentType,
        [property: JsonPropertyName("insecure_ssl")] string? InsecureSsl,
        [property: JsonPropertyName("url")] string? Url);

    private sealed record GitHubWebhookPatchRequest(
        [property: JsonPropertyName("content_type")] string ContentType,
        [property: JsonPropertyName("insecure_ssl")] string InsecureSsl,
        [property: JsonPropertyName("url")] string Url);
}
