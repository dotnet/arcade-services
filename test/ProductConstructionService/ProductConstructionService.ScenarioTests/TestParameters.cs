// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.InteropServices;
using Maestro.Common;
using Maestro.Common.AzureDevOpsTokens;
using Maestro.Common.Telemetry;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.MaestroConfiguration.Client;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Octokit.Internal;
using ProductConstructionService.ScenarioTests.Helpers;

#nullable enable
namespace ProductConstructionService.ScenarioTests;

[SetUpFixture]
public class TestParameters : IDisposable
{
    public static readonly string GitHubTestOrg = "maestro-auth-test";
    public static readonly string AzureDevOpsAccount = "dnceng";
    public static readonly string AzureDevOpsProject = "internal";
    public static readonly string GitHubUser = "dotnet-maestro-bot";
    public static readonly int AzureDevOpsBuildId = 144618;
    public static readonly int AzureDevOpsBuildDefinitionId = 6;

    private static readonly string _darcPackageSource;
    private static readonly string? _azDoToken;
    private static readonly string? _darcDir;
    private static readonly string? _darcVersion;
    private static readonly AzureDevOpsTokenProvider _azDoTokenProvider;

    private static TemporaryDirectory? _dir;
    private static string? _gitHubPath;

    public static string DarcExePath { get; private set; } = string.Empty;
    public static IProductConstructionServiceApi PcsApi { get; }
    public static Octokit.GitHubClient GitHubApi { get => field!; private set; }
    public static AzureDevOpsClient AzDoClient { get => field!; private set; }
    public static string PcsBaseUri { get; }
    public static string GitHubToken { get; }
    public static string AzDoToken => _azDoTokenProvider.GetTokenForAccount("default");
    public static bool IsCI { get; }
    public static string GitExePath => _gitHubPath!;
    public static List<string> BaseDarcRunArgs { get => field!; private set; }
    public static IServiceProvider ServiceProvider { get => field!; private set; }
    public static IConfigurationRepositoryParser ConfigRepoParser { get => field!; private set; }

    static TestParameters()
    {
        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<TestParameters>()
            .Build();

        PcsBaseUri = Environment.GetEnvironmentVariable("PCS_BASEURI")
            ?? userSecrets["PCS_BASEURI"]
            ?? "https://maestro.int-dot.net/";
        IsCI = Environment.GetEnvironmentVariable("DARC_IS_CI")?.ToLower() == "true";
        GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? userSecrets["GITHUB_TOKEN"]
            ?? TryGetGitHubTokenFromCliAsync().GetAwaiter().GetResult()
            ?? throw new Exception("Please configure the GitHub token");
        _darcPackageSource = Environment.GetEnvironmentVariable("DARC_PACKAGE_SOURCE") ?? userSecrets["DARC_PACKAGE_SOURCE"]
            ?? throw new Exception("Please configure the Darc package source");
        _azDoToken = Environment.GetEnvironmentVariable("AZDO_TOKEN")
            ?? userSecrets["AZDO_TOKEN"];
        _darcDir = Environment.GetEnvironmentVariable("DARC_DIR");
        _darcVersion = Environment.GetEnvironmentVariable("DARC_VERSION") ?? userSecrets["DARC_VERSION"];

        _azDoTokenProvider = AzureDevOpsTokenProvider.FromStaticOptions(new()
        {
            ["default"] = new()
            {
                Token = _azDoToken,
                DisableInteractiveAuth = IsCI,
            }
        });

        PcsApi = PcsBaseUri.Contains("localhost") || PcsBaseUri.Contains("127.0.0.1")
            ? PcsApiFactory.GetAnonymous(PcsBaseUri)
            : PcsApiFactory.GetAuthenticated(PcsBaseUri, accessToken: null, managedIdentityId: null, disableInteractiveAuth: IsCI);

        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILogger, NUnitLogger>();
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton<IProcessManager>(sp => ActivatorUtilities.CreateInstance<ProcessManager>(sp, "git"));
        services.AddSingleton<IRemoteTokenProvider>(new RemoteTokenProvider(AzDoToken, GitHubToken));
        services.AddSingleton<IAzureDevOpsTokenProvider>(_azDoTokenProvider);
        services.AddSingleton<ITelemetryRecorder, NoTelemetryRecorder>();
        services.AddSingleton<Microsoft.DotNet.DarcLib.IGitRepoFactory>(sp => ActivatorUtilities.CreateInstance<GitRepoFactory>(sp, Path.GetTempPath()));
        services.AddSingleton<IRemoteFactory, NoRemoteFactory>();
        services.AddSingleton<ILocalGitRepoFactory, LocalGitRepoFactory>();
        services.AddSingleton<ILocalGitClient, LocalGitClient>();
        services.AddSingleton<Microsoft.DotNet.MaestroConfiguration.Client.IGitRepoFactory, Microsoft.DotNet.DarcLib.ConfigurationRepository.GitRepoFactory>();
        services.AddSingleton<IConfigurationRepositoryParser, ConfigurationRepositoryParser>();
        ServiceProvider = services.BuildServiceProvider();
        ConfigRepoParser = ServiceProvider.GetRequiredService<IConfigurationRepositoryParser>();

        // TODO https://github.com/dotnet/arcade-services/issues/5693 remove this line
        Environment.SetEnvironmentVariable("DARC_USE_CONFIGURATION_REPOSITORY", "true");
    }

    [OneTimeSetUp]
    public async Task Initialize()
    {
        _dir = TemporaryDirectory.Get();
        var testDirSharedWrapper = Shareable.Create(_dir);

        var darcRootDir = _darcDir;
        if (string.IsNullOrEmpty(darcRootDir))
        {
            await InstallDarc(PcsApi, testDirSharedWrapper);
            darcRootDir = testDirSharedWrapper.Peek()!.Directory;
        }

        DarcExePath = Path.Join(darcRootDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "darc.exe" : "darc");
        _gitHubPath = await TestHelpers.Which("git");

        BaseDarcRunArgs =
        [
            "--bar-uri", PcsBaseUri,
            "--github-pat", GitHubToken,
            "--azdev-pat", AzDoToken,
            IsCI ? "--ci" : ""
        ];

        Assembly assembly = typeof(TestParameters).Assembly;
        GitHubApi =
            new Octokit.GitHubClient(
                new Octokit.ProductHeaderValue(assembly.GetName().Name, assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
                new InMemoryCredentialStore(new Octokit.Credentials(GitHubToken)));
        AzDoClient =
            new AzureDevOpsClient(
                _azDoTokenProvider,
                new ProcessManager(new NUnitLogger(), _gitHubPath),
                new NUnitLogger(),
                testDirSharedWrapper.TryTake()!.Directory);
    }

    private static async Task InstallDarc(IProductConstructionServiceApi pcsApi, Shareable<TemporaryDirectory> toolPath)
    {
        var darcVersion = _darcVersion ?? await pcsApi.Assets.GetDarcVersionAsync();
        var dotnetExe = await TestHelpers.Which("dotnet");

        var toolInstallArgs = new List<string>
        {
            "tool", "install",
            "--version", darcVersion,
            "--tool-path", toolPath.Peek()!.Directory,
            "Microsoft.DotNet.Darc",
        };

        if (!string.IsNullOrEmpty(_darcPackageSource))
        {
            toolInstallArgs.Add("--add-source");
            toolInstallArgs.Add(_darcPackageSource);
        }

        await TestHelpers.RunExecutableAsync(dotnetExe, [.. toolInstallArgs]);
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

    public void Dispose()
    {
        _dir?.Dispose();
    }
}
