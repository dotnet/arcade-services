// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Octokit.Internal;

namespace BuildInsights.ScenarioTests;

[SetUpFixture]
public class TestParameters
{
    public static readonly string GitHubTestOrg = "maestro-auth-test";
    public static readonly string AzureDevOpsAccount = "dnceng";
    public static readonly string AzureDevOpsProject = "internal";

    private static readonly string? _azDoToken;
    private static readonly AzureDevOpsTokenProvider _azDoTokenProvider;

    public static Octokit.GitHubClient GitHubApi { get => field!; private set; }
    public static AzureDevOpsClient AzDoClient { get => field!; private set; }
    public static string GitHubToken { get; }
    public static string AzDoToken => _azDoTokenProvider.GetTokenForAccount("default");
    public static bool IsCI { get; }
    public static ExponentialRetry ExponentialRetry => ServiceProvider.GetRequiredService<ExponentialRetry>();
    public static ISystemClock SystemClock => ServiceProvider.GetRequiredService<ISystemClock>();
    public static IServiceProvider ServiceProvider { get => field!; private set; }

    static TestParameters()
    {
        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<TestParameters>()
            .Build();

        IsCI = Environment.GetEnvironmentVariable("DARC_IS_CI")?.ToLower() == "true";
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

        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger, NUnitLogger>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
        services.AddSingleton<IRemoteTokenProvider>(_ => new RemoteTokenProvider(AzDoToken, GitHubToken));
        services.AddSingleton<IAzureDevOpsTokenProvider>(_azDoTokenProvider);
        services.AddSingleton<ITelemetryRecorder, NoTelemetryRecorder>();
        services.AddSingleton<IGitRepoFactory>(sp => ActivatorUtilities.CreateInstance<GitRepoFactory>(sp, Path.GetTempPath()));
        services.AddSingleton<IRemoteFactory, NoRemoteFactory>();
        services.AddSingleton<ILocalGitRepoFactory, LocalGitRepoFactory>();
        services.AddSingleton<ILocalGitClient, LocalGitClient>();
        ServiceProvider = services.BuildServiceProvider();
    }

    [OneTimeSetUp]
    public async Task Initialize()
    {
        Assembly assembly = typeof(TestParameters).Assembly;
        GitHubApi =
            new Octokit.GitHubClient(
                new Octokit.ProductHeaderValue(assembly.GetName().Name, assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
                new InMemoryCredentialStore(new Octokit.Credentials(GitHubToken)));
        AzDoClient =
            new AzureDevOpsClient(
                _azDoTokenProvider,
                new ProcessManager(new NUnitLogger(), "git"),
                new NUnitLogger(),
                Directory.CreateTempSubdirectory().FullName);
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

file class NoRemoteFactory : IRemoteFactory
{
    public Task<IDependencyFileManager> CreateDependencyFileManagerAsync(string repoUrl) => throw new NotImplementedException();
    public Task<IRemote> CreateRemoteAsync(string repoUrl) => throw new NotImplementedException();
}
