// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Runtime.InteropServices;
using Azure.Core;
using Maestro.Common.AzureDevOpsTokens;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Octokit.Internal;
using ProductConstructionService.Client;

#nullable enable
namespace ProductConstructionService.ScenarioTests;

[SetUpFixture]
public class TestParameters : IDisposable
{
    private static TemporaryDirectory? _dir;
    private static readonly string? pcsToken;
    private static readonly string darcPackageSource;
    private static readonly string? azDoToken;
    private static readonly string? darcDir;
    private static readonly string? darcVersion;
    private static IProductConstructionServiceApi? _pcsApi;
    private static AzureDevOpsTokenProvider? _azDoTokenProvider;
    private static Octokit.GitHubClient? _gitHubApi;
    private static AzureDevOpsClient? _azDoClient;
    private static string? _gitHubPath;
    private static List<string>? _baseDarcRunArgs;

    public static string DarcExePath { get; private set; } = string.Empty;
    public static IProductConstructionServiceApi PcsApi => _pcsApi!;
    public static Octokit.GitHubClient GitHubApi => _gitHubApi!;
    public static AzureDevOpsClient AzDoClient => _azDoClient!;
    public static string PcsBaseUri { get; private set; }
    public static string GitHubToken { get; private set; }
    public static string AzDoToken => _azDoTokenProvider!.GetTokenForAccount("default");
    public static bool IsCI { get; private set; }
    public static string? PcsToken => PcsApi.Options.Credentials?.GetToken(new TokenRequestContext(), default).Token;
    public static string GitHubTestOrg => "maestro-auth-test";
    public static string AzureDevOpsAccount => "dnceng";
    public static string AzureDevOpsProject => "internal";
    public static string GitHubUser => "dotnet-maestro-bot";
    public static string GitExePath => _gitHubPath!;
    public static int AzureDevOpsBuildId => 144618;
    public static int AzureDevOpsBuildDefinitionId => 6;
    public static List<string> BaseDarcRunArgs => _baseDarcRunArgs!;

    static TestParameters()
    {
        IConfiguration userSecrets = new ConfigurationBuilder()
            .AddUserSecrets<TestParameters>()
            .Build();

        PcsBaseUri = Environment.GetEnvironmentVariable("PCS_BASEURI")
            ?? userSecrets["PCS_BASEURI"]
            ?? "https://product-construction-int.delightfuldune-c0f01ab0.westus2.azurecontainerapps.io/";
        pcsToken = Environment.GetEnvironmentVariable("PCS_TOKEN")
            ?? userSecrets["PCS_TOKEN"];
        IsCI = Environment.GetEnvironmentVariable("DARC_IS_CI")?.ToLower() == "true";
        GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? userSecrets["GITHUB_TOKEN"]
            ?? throw new Exception("Please configure the GitHub token");
        darcPackageSource = Environment.GetEnvironmentVariable("DARC_PACKAGE_SOURCE") ?? userSecrets["DARC_PACKAGE_SOURCE"]
            ?? throw new Exception("Please configure the Darc package source");
        azDoToken = Environment.GetEnvironmentVariable("AZDO_TOKEN")
            ?? userSecrets["AZDO_TOKEN"];
        darcDir = Environment.GetEnvironmentVariable("DARC_DIR");
        darcVersion = Environment.GetEnvironmentVariable("DARC_VERSION") ?? userSecrets["DARC_VERSION"];
    }

    [OneTimeSetUp]
    public async Task Initialize()
    {
        _dir = TemporaryDirectory.Get();
        var testDirSharedWrapper = Shareable.Create(_dir);

        _pcsApi = PcsBaseUri.Contains("localhost") || PcsBaseUri.Contains("127.0.0.1")
            ? PcsApiFactory.GetAnonymous(PcsBaseUri)
            : PcsApiFactory.GetAuthenticated(PcsBaseUri, accessToken: pcsToken, managedIdentityId: null, disableInteractiveAuth: IsCI);

        var darcRootDir = darcDir;
        if (string.IsNullOrEmpty(darcRootDir))
        {
            await InstallDarc(_pcsApi, testDirSharedWrapper);
            darcRootDir = testDirSharedWrapper.Peek()!.Directory;
        }

        DarcExePath = Path.Join(darcRootDir, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "darc.exe" : "darc");
        _gitHubPath = await TestHelpers.Which("git");
        _azDoTokenProvider = AzureDevOpsTokenProvider.FromStaticOptions(new()
        {
            ["default"] = new()
            {
                Token = azDoToken,
                UseLocalCredentials = !IsCI,
                DisableInteractiveAuth = IsCI,
            }
        });

        _baseDarcRunArgs = [
            "--bar-uri", TestParameters.PcsBaseUri,
            "--github-pat", TestParameters.GitHubToken,
            "--azdev-pat", TestParameters.AzDoToken,
            TestParameters.IsCI ? "--ci" : ""
        ];
        if (!string.IsNullOrEmpty(TestParameters.PcsToken))
        {
            _baseDarcRunArgs.AddRange(["--p", TestParameters.PcsToken]);
        }

        Assembly assembly = typeof(TestParameters).Assembly;
        _gitHubApi =
            new Octokit.GitHubClient(
                new Octokit.ProductHeaderValue(assembly.GetName().Name, assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
                new InMemoryCredentialStore(new Octokit.Credentials(GitHubToken)));
        _azDoClient =
            new AzureDevOpsClient(
                _azDoTokenProvider,
                new ProcessManager(new NUnitLogger(), _gitHubPath),
                new NUnitLogger(),
                testDirSharedWrapper.TryTake()!.Directory);
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

    public void Dispose()
    {
        _dir?.Dispose();
    }
}
