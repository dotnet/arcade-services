// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.Extensions.Configuration;
using Octokit.Internal;

#nullable enable
namespace Maestro.ScenarioTests;

public class TestParameters : IDisposable
{
    internal readonly TemporaryDirectory _dir;

    private static readonly string[] maestroBaseUris;
    private static readonly string? maestroToken;
    private static readonly string githubToken;
    private static readonly string darcPackageSource;
    private static readonly string? azdoToken;
    private static readonly bool isCI;
    private static readonly string? darcDir;

    static TestParameters()
    {
        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<TestParameters>()
            .Build();

        maestroBaseUris = (Environment.GetEnvironmentVariable("MAESTRO_BASEURIS")
                ?? userSecrets["MAESTRO_BASEURIS"]
                ?? "https://maestro.int-dot.net")
            .Split(',');
        maestroToken = Environment.GetEnvironmentVariable("MAESTRO_TOKEN") ?? userSecrets["MAESTRO_TOKEN"];
        isCI = Environment.GetEnvironmentVariable("DARC_IS_CI")?.ToLower() == "true";
        githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? userSecrets["GITHUB_TOKEN"]
            ?? throw new Exception("Please configure the GitHub token");
        darcPackageSource = Environment.GetEnvironmentVariable("DARC_PACKAGE_SOURCE")
            ?? throw new Exception("Please configure the Darc package source");
        azdoToken = Environment.GetEnvironmentVariable("AZDO_TOKEN") ?? userSecrets["AZDO_TOKEN"];
        darcDir = Environment.GetEnvironmentVariable("DARC_DIR");
    }

    /// <param name="useNonPrimaryEndpoint">If set to true, the test will attempt to use the non primary endpoint, if provided</param>
    public static async Task<TestParameters> GetAsync(bool useNonPrimaryEndpoint = false)
    {
        var testDir = TemporaryDirectory.Get();
        var testDirSharedWrapper = Shareable.Create(testDir);

        var maestroBaseUri = useNonPrimaryEndpoint
            ? maestroBaseUris.Last()
            : maestroBaseUris.First();

        IMaestroApi maestroApi = MaestroApiFactory.GetAuthenticated(
            maestroBaseUri,
            maestroToken,
            managedIdentityId: null,
            disableInteractiveAuth: isCI);

        string? darcRootDir = darcDir;
        if (string.IsNullOrEmpty(darcRootDir))
        {
            await InstallDarc(maestroApi, testDirSharedWrapper);
            darcRootDir = testDirSharedWrapper.Peek()!.Directory;
        }

        string darcExe = Path.Join(darcRootDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "darc.exe" : "darc");
        string git = await TestHelpers.Which("git");
        var tokenProvider = AzureDevOpsTokenProvider.FromStaticOptions(new()
        {
            ["default"] = new()
            {
                Token = azdoToken,
                UseLocalCredentials = !isCI,
            }
        });

        Assembly assembly = typeof(TestParameters).Assembly;
        var githubApi =
            new Octokit.GitHubClient(
                new Octokit.ProductHeaderValue(assembly.GetName().Name, assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
                new InMemoryCredentialStore(new Octokit.Credentials(githubToken)));
        var azDoClient =
            new AzureDevOpsClient(
                tokenProvider,
                new ProcessManager(new NUnitLogger(), git),
                new NUnitLogger(),
                testDirSharedWrapper.TryTake()!.Directory);

        return new TestParameters(
            darcExe, git, maestroBaseUri, maestroToken, githubToken, maestroApi, githubApi, azDoClient, testDir, tokenProvider, isCI);
    }

    private static async Task InstallDarc(IMaestroApi maestroApi, Shareable<TemporaryDirectory> toolPath)
    {
        string darcVersion = await maestroApi.Assets.GetDarcVersionAsync();
        string dotnetExe = await TestHelpers.Which("dotnet");

        var toolInstallArgs = new List<string>
        {
            "tool", "install",
            "--version", darcVersion,
            "--tool-path", toolPath.Peek()!.Directory,
            "Microsoft.DotNet.Darc",
        };

        if (!string.IsNullOrEmpty(darcPackageSource))
        {
            toolInstallArgs.Add("--add-source");
            toolInstallArgs.Add(darcPackageSource);
        }

        await TestHelpers.RunExecutableAsync(dotnetExe, [.. toolInstallArgs]);
    }

    private TestParameters(
        string darcExePath,
        string gitExePath,
        string maestroBaseUri,
        string? maestroToken,
        string gitHubToken,
        IMaestroApi maestroApi,
        Octokit.GitHubClient gitHubApi,
        AzureDevOpsClient azdoClient,
        TemporaryDirectory dir,
        AzureDevOpsTokenProvider azdoTokenProvider,
        bool isCI)
    {
        _dir = dir;
        DarcExePath = darcExePath;
        GitExePath = gitExePath;
        MaestroBaseUri = maestroBaseUri;
        MaestroToken = maestroToken;
        GitHubToken = gitHubToken;
        MaestroApi = maestroApi;
        GitHubApi = gitHubApi;
        AzDoClient = azdoClient;
        IsCI = isCI;
        _azdoTokenProvider = azdoTokenProvider;
    }

    public string DarcExePath { get; }

    public string GitExePath { get; }

    public string GitHubUser { get; } = "dotnet-maestro-bot";

    public string GitHubTestOrg { get; } = "maestro-auth-test";

    public string MaestroBaseUri { get; }

    public string? MaestroToken { get; }

    public string GitHubToken { get; }

    public IMaestroApi MaestroApi { get; }

    public Octokit.GitHubClient GitHubApi { get; }

    public AzureDevOpsClient AzDoClient { get; }

    public int AzureDevOpsBuildDefinitionId { get; } = 6;

    public int AzureDevOpsBuildId { get; } = 144618;

    public string AzureDevOpsAccount { get; } = "dnceng";

    public string AzureDevOpsProject { get; } = "internal";

    public bool IsCI { get; }

    private readonly AzureDevOpsTokenProvider _azdoTokenProvider;

    public void Dispose()
    {
        _dir?.Dispose();
    }

    public string GetAzDoToken()
    {
        return _azdoTokenProvider.GetTokenForAccount(AzureDevOpsAccount);
    }
}
