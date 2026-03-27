// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using BuildInsights.AzureStorage.Cache;
using BuildInsights.GitHub;
using BuildInsights.ServiceDefaults;
using BuildInsights.ServiceDefaults.Configuration.Models;
using BuildInsights.Utilities.AzureDevOps;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.ApplicationInsights;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Octokit.Internal;
using static BuildInsights.ServiceDefaults.BuildInsightsCommonConfiguration;

namespace BuildInsights.ScenarioTests;

[SetUpFixture]
public class ScenarioTestConfiguration
{
    public static readonly string GitHubTestOrg = "maestro-auth-test";
    public static readonly string GitHubTestRepo = "build-result-analysis-test";

    private static readonly string? _azDoToken;
    private static readonly AzureDevOpsTokenProvider _azDoTokenProvider;

    public static string EnvironmentName { get; }
    public static string GitHubToken { get; }
    public static bool IsCI { get; }
    public static ExponentialRetry ExponentialRetry => ServiceProvider.GetRequiredService<ExponentialRetry>();
    public static IServiceProvider ServiceProvider { get => field; private set; }
    public static Octokit.GitHubClient GitHubApi { get => field; private set; }
    public static GitHubAppSettings GitHubAppSettings => ServiceProvider.GetRequiredService<IOptions<GitHubAppSettings>>().Value;

    static ScenarioTestConfiguration()
    {
        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<ScenarioTestConfiguration>()
            .Build();

        EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Development";
        IsCI = Environment.GetEnvironmentVariable("IS_CI")?.ToLower() == "true";
        GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? userSecrets["GITHUB_TOKEN"]
            ?? TryGetGitHubTokenFromCliAsync().GetAwaiter().GetResult()
            ?? throw new Exception("Please configure the GitHub token");
        _azDoToken = Environment.GetEnvironmentVariable("AZDO_TOKEN")
            ?? userSecrets["AZDO_TOKEN"];
        _azDoTokenProvider = AzureDevOpsTokenProvider.FromStaticOptions(new()
        {
            ["default"] = new()
            {
                Token = _azDoToken,
                DisableInteractiveAuth = IsCI,
            }
        });

        var assembly = Assembly.GetExecutingAssembly();
        var assemblyDir = Path.GetDirectoryName(assembly.Location)!;

        var configurationManager = new ConfigurationManager();
        configurationManager.AddSharedConfiguration(assemblyDir, EnvironmentName);
        configurationManager.AddJsonFile(Path.Combine(assemblyDir, "appsettings.json"), optional: true);
        configurationManager.AddJsonFile(Path.Combine(assemblyDir, $"appsettings.{EnvironmentName}.json"), optional: true);
        IServiceCollection services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configurationManager);
        services.Configure<AzureDevOpsTokenProviderOptions>(ConfigurationKeys.AzureDevOpsConfiguration, (o, s) => s.Bind(o));
        services.Configure<GitHubAppSettings>(ConfigurationKeys.GitHubApp, (o, s) => s.Bind(o));

        services.AddVssConnection();
        services.AddSingleton<IGitHubApplicationClientFactory, TestGitHubClientFactory>();
        services.AddGitHubTokenProvider();
        services.AddGitHub();
        services.AddScoped<IRemoteTokenProvider>(static _ => new RemoteTokenProvider(_azDoTokenProvider, new ResolvedTokenProvider(GitHubToken)));
        services.AddScoped<IAzureDevOpsTokenProvider, AzureDevOpsTokenProvider>();
        services.AddBlobStorageCaching(configurationManager.GetRequiredSection(ConfigurationKeys.BlobStorage));
        services.AddLogging();
        services.AddSingleton<ExponentialRetry>();
        services.AddSingleton<ILogger, NUnitLogger>();
        services.AddTransient<ISystemClock, SystemClock>();
        services.AddSingleton<ExponentialRetry>();
        services.AddSingleton<TelemetryClient>();
        services.Configure<ExponentialRetryOptions>(_ => { });
        services.AddMemoryCache();
        ServiceProvider = services.BuildServiceProvider().CreateScope().ServiceProvider;
        GitHubApi = new Octokit.GitHubClient(
            new Octokit.ProductHeaderValue(assembly.GetName().Name, assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
            new InMemoryCredentialStore(new Octokit.Credentials(GitHubToken)));
    }

    private static async Task<string?> TryGetGitHubTokenFromCliAsync()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger(nameof(ProcessManager));
        try
        {
            var processManager = new ProcessManager(logger, "git");
            var result = await processManager.Execute("gh", ["auth", "token"], timeout: TimeSpan.FromSeconds(15));

            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                var token = result.StandardOutput.Trim();
                logger.LogDebug("Successfully retrieved GitHub token from 'gh auth token'");
                return token;
            }

            logger.LogDebug("GitHub CLI did not return a valid token. Exit code: {exitCode}", result.ExitCode);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to retrieve GitHub token from 'gh' CLI. This is expected if 'gh' is not installed or not authenticated.");
            return null;
        }
    }
}
