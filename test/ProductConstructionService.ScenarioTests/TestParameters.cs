// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.InteropServices;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Configuration;
using Octokit.Internal;
using ProductConstructionService.Client;

#nullable enable
namespace ProductConstructionService.ScenarioTests;

public class TestParameters : IDisposable
{
    internal readonly TemporaryDirectory _dir;

    private static readonly string pcsBaseUri;
    private static readonly string? pcsToken;
    private static readonly string githubToken;
    private static readonly string darcPackageSource;
    private static readonly string azdoToken;
    private static readonly bool isCI;
    private static readonly string? darcDir;
    private static readonly string? darcVersion;

    static TestParameters()
    {
        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<TestParameters>()
            .Build();

        pcsBaseUri = Environment.GetEnvironmentVariable("PCS_BASEURI")
            ?? userSecrets["PCS_BASEURI"]
            ?? "https://product-construction-int.delightfuldune-c0f01ab0.westus2.azurecontainerapps.io/";
        pcsToken = Environment.GetEnvironmentVariable("PCS_TOKEN") ?? userSecrets["PCS_TOKEN"];
        isCI = Environment.GetEnvironmentVariable("DARC_IS_CI")?.ToLower() == "true";
        githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? userSecrets["GITHUB_TOKEN"]
            ?? throw new Exception("Please configure the GitHub token");
        darcPackageSource = Environment.GetEnvironmentVariable("DARC_PACKAGE_SOURCE") ?? userSecrets["DARC_PACKAGE_SOURCE"]
            ?? throw new Exception("Please configure the Darc package source");
        azdoToken = Environment.GetEnvironmentVariable("AZDO_TOKEN") ?? userSecrets["AZDO_TOKEN"]
            ?? throw new Exception("Please configure the Azure DevOps token");
        darcDir = Environment.GetEnvironmentVariable("DARC_DIR");
        darcVersion = Environment.GetEnvironmentVariable("DARC_VERSION") ?? userSecrets["DARC_VERSION"];

    }

    /// <param name="useNonPrimaryEndpoint">If set to true, the test will attempt to use the non primary endpoint, if provided</param>
    public static async Task<TestParameters> GetAsync(bool useNonPrimaryEndpoint = false)
    {
        var testDir = TemporaryDirectory.Get();
        var testDirSharedWrapper = Shareable.Create(testDir);

        IProductConstructionServiceApi pcsApi = pcsToken != null
            ? PcsApiFactory.GetAuthenticated(pcsBaseUri, pcsToken, managedIdentityId: null, disableInteractiveAuth: isCI)
            : PcsApiFactory.GetAnonymous(pcsBaseUri);

        var darcRootDir = darcDir;
        if (string.IsNullOrEmpty(darcRootDir))
        {
            await InstallDarc(pcsApi, testDirSharedWrapper);
            darcRootDir = testDirSharedWrapper.Peek()!.Directory;
        }

        var darcExe = Path.Join(darcRootDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "darc.exe" : "darc");
        var git = await TestHelpers.Which("git");
        var tokenProvider = AzureDevOpsTokenProvider.FromStaticOptions(new()
        {
            ["default"] = new()
            {
                Token = azdoToken
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
            darcExe, git, pcsBaseUri, pcsToken, githubToken, pcsApi, githubApi, azDoClient, testDir, azdoToken, isCI);
    }

    private static async Task InstallDarc(IProductConstructionServiceApi pcsApi, Shareable<TemporaryDirectory> toolPath)
    {
        var darcVersion = TestParameters.darcVersion ?? await pcsApi.Assets.GetDarcVersionAsync();
        var dotnetExe = await TestHelpers.Which("dotnet");

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
        string pcsBaseUri,
        string? pcsToken,
        string gitHubToken,
        IProductConstructionServiceApi pcsApi,
        Octokit.GitHubClient gitHubApi,
        AzureDevOpsClient azdoClient,
        TemporaryDirectory dir,
        string azdoToken,
        bool isCI)
    {
        _dir = dir;
        DarcExePath = darcExePath;
        GitExePath = gitExePath;
        MaestroBaseUri = pcsBaseUri;
        MaestroToken = pcsToken;
        GitHubToken = gitHubToken;
        PcsApi = pcsApi;
        GitHubApi = gitHubApi;
        AzDoClient = azdoClient;
        AzDoToken = azdoToken;
        IsCI = isCI;
    }

    public string DarcExePath { get; }

    public string GitExePath { get; }

    public string GitHubUser { get; } = "dotnet-maestro-bot";

    public string GitHubTestOrg { get; } = "maestro-auth-test";

    public string MaestroBaseUri { get; }

    public string? MaestroToken { get; }

    public string GitHubToken { get; }

    public IProductConstructionServiceApi PcsApi { get; }

    public Octokit.GitHubClient GitHubApi { get; }

    public AzureDevOpsClient AzDoClient { get; }

    public int AzureDevOpsBuildDefinitionId { get; } = 6;

    public int AzureDevOpsBuildId { get; } = 144618;

    public string AzureDevOpsAccount { get; } = "dnceng";

    public string AzureDevOpsProject { get; } = "internal";

    public string AzDoToken { get; }

    public bool IsCI { get; }

    public void Dispose()
    {
        _dir?.Dispose();
    }
}
