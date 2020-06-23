using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.Extensions.Configuration;
using Octokit;
using Octokit.Internal;

namespace Maestro.ScenarioTests
{
    public class TestParameters : IDisposable
    {
        internal readonly TemporaryDirectory _dir;

        public static async Task<TestParameters> GetAsync()
        {
            IConfiguration userSecrets = new ConfigurationBuilder()
                .AddUserSecrets<TestParameters>()
                .Build();

            string maestroBaseUri = Environment.GetEnvironmentVariable("MAESTRO_BASEURI") ?? "https://maestro-int.westus2.cloudapp.azure.com";
            string maestroToken = Environment.GetEnvironmentVariable("MAESTRO_TOKEN") ?? userSecrets["MAESTRO_TOKEN"];
            string githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? userSecrets["GITHUB_TOKEN"];
            string darcPackageSource = Environment.GetEnvironmentVariable("DARC_PACKAGE_SOURCE");
            string azdoToken = Environment.GetEnvironmentVariable("AZDO_TOKEN") ?? userSecrets["AZDO_TOKEN"];

            using var testDir = Shareable.Create(TemporaryDirectory.Get());

            IMaestroApi maestroApi = maestroToken == null
                ? ApiFactory.GetAnonymous(maestroBaseUri)
                : ApiFactory.GetAuthenticated(maestroBaseUri, maestroToken);

            string darcVersion = await maestroApi.Assets.GetDarcVersionAsync();
            string dotnetExe = await TestHelpers.Which("dotnet");

            var toolInstallArgs = new List<string>
            {
                "tool", "install",
                "--tool-path", testDir.Peek()!.Directory,
                "--version", darcVersion,
                "Microsoft.DotNet.Darc",
            };
            if (!string.IsNullOrEmpty(darcPackageSource))
            {
                toolInstallArgs.Add("--add-source");
                toolInstallArgs.Add(darcPackageSource);
            }
            await TestHelpers.RunExecutableAsync(dotnetExe, toolInstallArgs.ToArray());

            string darcExe = Path.Join(testDir.Peek()!.Directory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "darc.exe" : "darc");

            Assembly assembly = typeof(TestParameters).Assembly;
            var githubApi =
                new GitHubClient(
                    new ProductHeaderValue(assembly.GetName().Name, assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
                    new InMemoryCredentialStore(new Credentials(githubToken)));
            var azDoClient =
                new Microsoft.DotNet.DarcLib.AzureDevOpsClient(await TestHelpers.Which("git"), azdoToken, null, testDir.TryTake()!.Directory);

            return new TestParameters(darcExe, await TestHelpers.Which("git"), maestroBaseUri, maestroToken!, githubToken, maestroApi, githubApi, azDoClient, testDir.TryTake()!, azdoToken);
        }

        private TestParameters(string darcExePath, string gitExePath, string maestroBaseUri, string maestroToken, string gitHubToken,
            IMaestroApi maestroApi, GitHubClient gitHubApi, Microsoft.DotNet.DarcLib.AzureDevOpsClient azdoClient, TemporaryDirectory dir, string azdoToken)
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
            AzDoToken = azdoToken;
        }

        public string DarcExePath { get; }

        public string GitExePath { get; }

        public string GitHubUser { get; } = "dotnet-maestro-bot";

        public string GitHubTestOrg { get; } = "maestro-auth-test";

        public string MaestroBaseUri { get; }

        public string MaestroToken { get; }

        public string GitHubToken { get; }

        public IMaestroApi MaestroApi { get; }

        public GitHubClient GitHubApi { get; }

        public Microsoft.DotNet.DarcLib.AzureDevOpsClient AzDoClient { get; }

        public int AzureDevOpsBuildDefinitionId { get; } = 6;

        public int AzureDevOpsBuildId { get; } = 144618;

        public string AzureDevOpsAccount { get; } = "dnceng";

        public string AzureDevOpsProject { get; } = "internal";

        public string AzDoToken { get; }

        public void Dispose()
        {
            if (_dir != null)
            {
                _dir.Dispose();
            }
        }
    }
}
