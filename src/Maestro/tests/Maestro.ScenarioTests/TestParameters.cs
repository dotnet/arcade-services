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
using Xunit.Abstractions;

namespace Maestro.ScenarioTests
{
    public class TestParameters : IDisposable
    {
        private readonly TemporaryDirectory _dir;

        public static async Task<TestParameters> GetAsync(ITestOutputHelper testOutput)
        {
            var userSecrets = new ConfigurationBuilder()
                .AddUserSecrets<TestParameters>()
                .Build();

            var maestroBaseUri = Environment.GetEnvironmentVariable("MAESTRO_BASEURI") ?? "https://maestro-int.westus2.cloudapp.azure.com";
            var maestroToken = Environment.GetEnvironmentVariable("MAESTRO_TOKEN") ?? userSecrets["MAESTRO_TOKEN"];
            var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? userSecrets["GITHUB_TOKEN"];
            var darcPackageSource = Environment.GetEnvironmentVariable("DARC_PACKAGE_SOURCE");

            using var testDir = Shareable.Create(TemporaryDirectory.Get());

            var maestroApi = maestroToken == null
                ? ApiFactory.GetAnonymous(maestroBaseUri)
                : ApiFactory.GetAuthenticated(maestroBaseUri, maestroToken);

            var darcVersion = await maestroApi.Assets.GetDarcVersionAsync();
            var dotnetExe = await TestHelpers.Which(testOutput, "dotnet");

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
            await TestHelpers.RunExecutableAsync(testOutput, dotnetExe, toolInstallArgs.ToArray());

            var darcExe = Path.Join(testDir.Peek()!.Directory, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "darc.exe" : "darc");

            var assembly = typeof(TestParameters).Assembly;
            var githubApi =
                new GitHubClient(
                    new ProductHeaderValue(assembly.GetName().Name, assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
                    new InMemoryCredentialStore(new Credentials(githubToken)));

            return new TestParameters(darcExe, await TestHelpers.Which(testOutput, "git"), maestroBaseUri, maestroToken!, githubToken, maestroApi, githubApi, testDir.TryTake()!);
        }

        private TestParameters(string darcExePath, string gitExePath, string maestroBaseUri, string maestroToken, string gitHubToken, IMaestroApi maestroApi, GitHubClient gitHubApi, TemporaryDirectory dir)
        {
            _dir = dir;
            DarcExePath = darcExePath;
            GitExePath = gitExePath;
            MaestroBaseUri = maestroBaseUri;
            MaestroToken = maestroToken;
            GitHubToken = gitHubToken;
            MaestroApi = maestroApi;
            GitHubApi = gitHubApi;
        }

        public string DarcExePath { get; }

        public string GitExePath { get;  }

        public string GitHubUser { get; } = "dotnet-maestro-bot";

        public string GitHubTestOrg { get; } = "maestro-auth-test";

        public string MaestroBaseUri { get; }

        public string MaestroToken { get; }

        public string GitHubToken { get; }

        public IMaestroApi MaestroApi { get; }

        public GitHubClient GitHubApi { get; }

        public void Dispose()
        {
            _dir.Dispose();
        }
    }
}
