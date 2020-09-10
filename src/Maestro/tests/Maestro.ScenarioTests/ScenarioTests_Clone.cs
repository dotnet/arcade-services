using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Maestro.ScenarioTests
{
    [TestFixture]
    [NonParallelizable]
    [Category("PostDeployment")]
    class ScenarioTests_Clone : MaestroScenarioTestBase
    {
        [Test]
        public async Task Darc_CloneRepo()
        {
            TestContext.WriteLine("Darc-Clone repo end to end test");

            TestParameters parameters = await TestParameters.GetAsync();
            SetTestParameters(parameters);

            string sourceRepoName = "core-sdk";
            string sourceRepoVersion = "v3.0.100-preview4-011223";
            string sourceRepoUri = GetRepoUrl(sourceRepoName);

            // these repos are not currently clonable for us due to auth
            string reposToIgnore = "https://dev.azure.com/dnceng/internal/_git/dotnet-optimization;https://dev.azure.com/devdiv/DevDiv/_git/DotNet-Trusted;https://devdiv.visualstudio.com/DevDiv/_git/DotNet-Trusted";

            // these repos have file names that are too long on Windows for the temp folder
            reposToIgnore += ";https://github.com/aspnet/AspNetCore;https://github.com/aspnet/AspNetCore-Tooling;https://github.com/dotnet/core-setup;https://github.com/dotnet/templating;" +
                "https://github.com/dotnet/sdk;https://github.com/Microsoft/visualfsharp;https://github.com/dotnet/roslyn;https://github.com/NuGet/NuGet.Client;https://github.com/dotnet/corefx";

            Dictionary<string, string> expectedRepos1 = new Dictionary<string, string>
            {
                { "cli.204f425b6f061d0b8a01faf46f762ecf71436f68",                     "BB7B735A345157E1CB90E4E03340FD202C798B06E5EA089C2F0191562D9DF1B4" },
                { "cliCommandLineParser.0e89c2116ad28e404ba56c14d1c3f938caa25a01",    "CCA267C3FB69E8AADFA6CEE474249ACB84829D9CCFE7F9F5D7F17EFA87A26739" },
                { "core-sdk.v3.0.100-preview4-011223",                                "BBAE24214F40518798126A2F4729C38B6C3B67886DCDE0C2D64BBE203D4ACFBB" },
                { "msbuild.d004974104fde202e633b3c97e0ece3287aa62f9",                 "FD4F85D3FB60B5EBC105647E290924071A240A0C6FD49E3D44A2BC38A797946C" },
                { "standard.8ef5ada20b5343b0cb9e7fd577341426dab76cd8",                "FA876A709FDB0CD3D864431B2887D2BC9ACBFC313E8C452A8636EE7FAE4E5D02" },
                { "toolset.3165b2579582b6b44aef768c01c3cc9ff4f0bc17",                 "D483493BF79129FD2DADC39C51FACB5FA363CEB03E54ECD180720DE2D95EDED9" },
                { "websdk.b55d4f4cf22bee7ec9a2ca5f49d54ebf6ee67e83",                  "A51DEB15209039D17FC0D781BFC445F8C5CDC1D51673AC4C0929FBEE8C1E4D21" },
                { "winforms.b1ee29b8b8e14c1200adff02847391dde471d0d2",                "1288479D12130F9AEF56137DCC655B5B59277F5153172B30DC419456AF9CA011" },
                { "wpf.d378b1ec6b8555c52b7da1c40ffc0784cb0f5cad",                     "CFE5E19366FD5A90101A8F369CFC70611F9B1439A0720D435EE34872AF55A40A" }
            };

            string[] expectedMasterRepos1 = new string[]
            {
                "cli",
                "cliCommandLineParser",
                "core-sdk",
                "msbuild",
                "standard",
                "toolset",
                "websdk",
                "winforms",
                "wpf"
            };

            string[] expectedGitDirs1 = new string[]
            {
                "cli.git",
                "cliCommandLineParser.git",
                "core-sdk.git",
                "msbuild.git",
                "standard.git",
                "toolset.git",
                "websdk.git",
                "winforms.git",
                "wpf.git"
            };

            TestContext.WriteLine($"parameters: sourceRepoName={sourceRepoName}, sourceRepoVersion={sourceRepoVersion}, reposToIgnore='{reposToIgnore}'");
            TestContext.WriteLine($"Cloning repo {sourceRepoUri} at {sourceRepoVersion} with depth 2 and include - toolset = false");
            TemporaryDirectory reposFolder = await CloneRepositoryWithDarc(sourceRepoName, sourceRepoVersion, reposToIgnore, false, 2);
            CheckExpectedClonedRepos(expectedRepos1, expectedMasterRepos1, expectedGitDirs1, reposFolder);

            Dictionary<string, string> expectedRepos2 = new Dictionary<string, string>
            {
                { "cli.204f425b6f061d0b8a01faf46f762ecf71436f68",                     "BB7B735A345157E1CB90E4E03340FD202C798B06E5EA089C2F0191562D9DF1B4" },
                { "cliCommandLineParser.0e89c2116ad28e404ba56c14d1c3f938caa25a01",    "CCA267C3FB69E8AADFA6CEE474249ACB84829D9CCFE7F9F5D7F17EFA87A26739" },
                { "core-sdk.v3.0.100-preview4-011223",                                "BBAE24214F40518798126A2F4729C38B6C3B67886DCDE0C2D64BBE203D4ACFBB" },
                { "coreclr.d833cacabd67150fe3a2405845429a0ba1b72c12",                 "5A69F61C354C1DE9B839D6922CE866AB0B760BCCBC919EE4C408D44B6A40084C" },
                { "msbuild.d004974104fde202e633b3c97e0ece3287aa62f9",                 "FD4F85D3FB60B5EBC105647E290924071A240A0C6FD49E3D44A2BC38A797946C" },
                { "standard.8ef5ada20b5343b0cb9e7fd577341426dab76cd8",                "FA876A709FDB0CD3D864431B2887D2BC9ACBFC313E8C452A8636EE7FAE4E5D02" },
                { "toolset.3165b2579582b6b44aef768c01c3cc9ff4f0bc17",                 "D483493BF79129FD2DADC39C51FACB5FA363CEB03E54ECD180720DE2D95EDED9" },
                { "websdk.b55d4f4cf22bee7ec9a2ca5f49d54ebf6ee67e83",                  "A51DEB15209039D17FC0D781BFC445F8C5CDC1D51673AC4C0929FBEE8C1E4D21" },
                { "winforms.b1ee29b8b8e14c1200adff02847391dde471d0d2",                "1288479D12130F9AEF56137DCC655B5B59277F5153172B30DC419456AF9CA011" },
                {"wpf.d378b1ec6b8555c52b7da1c40ffc0784cb0f5cad",                      "CFE5E19366FD5A90101A8F369CFC70611F9B1439A0720D435EE34872AF55A40A" }
            };

            string[] expectedMasterRepos2 = new string[]
            {
                "cli",
                "cliCommandLineParser",
                "core-sdk",
                "coreclr",
                "msbuild",
                "standard",
                "toolset",
                "websdk",
                "winforms",
                "wpf"
            };

            string[] expectedGitDirs2 = new string[]
            {
                "cli.git",
                "cliCommandLineParser.git",
                "core-sdk.git",
                "coreclr.git",
                "msbuild.git",
                "standard.git",
                "toolset.git",
                "websdk.git",
                "winforms.git",
                "wpf.git"
            };

            reposToIgnore += ";https://github.com/dotnet/arcade";
            TestContext.WriteLine($"Cloning repo {sourceRepoUri} at {sourceRepoVersion} with depth 4 and include-toolset=true");
            reposFolder = await CloneRepositoryWithDarc(sourceRepoName, sourceRepoVersion, reposToIgnore, true, 4);
            CheckExpectedClonedRepos(expectedRepos2, expectedMasterRepos2, expectedGitDirs2, reposFolder);
        }

        private void CheckExpectedClonedRepos(Dictionary<string, string> expectedRepos, string[] expectedMasterRepos, string[] expectedGitDirs, TemporaryDirectory reposFolder)
        {
            using (ChangeDirectory(reposFolder.Directory))
            {
                string clonedReposFolder = Path.Join(reposFolder.Directory, "cloned-repos");
                string gitDirFolder = Path.Join(reposFolder.Directory, "git-dirs");

                TestContext.WriteLine("Validate the hash of the Version.Details.xml matches expected");
                foreach (string name in expectedRepos.Keys)
                {
                    string path = Path.Join(clonedReposFolder, name);
                    DirectoryAssert.Exists(path, $"Expected cloned repo '{name}' but not found at {path}");

                    string versionPath = Path.Join(path, "eng", "Version.Details.xml");
                    FileAssert.Exists(versionPath, $"Expected a file at {versionPath}");

                    using (FileStream stream = File.OpenRead(versionPath))
                    {
                        using SHA256 hashGenerator = SHA256.Create();
                        byte[] fileHash = hashGenerator.ComputeHash(stream);
                        string fileHashInHex = BitConverter.ToString(fileHash).Replace("-", "");
                        Assert.AreEqual(expectedRepos[name], fileHashInHex, $"Expected {versionPath} to have hash '{expectedRepos[name]}', actual hash '{fileHash}'");
                    }
                }

                TestContext.WriteLine("Ensure the presence of the git directories");
                foreach (string repo in expectedMasterRepos)
                {
                    string path = Path.Join(clonedReposFolder, repo);
                    DirectoryAssert.Exists(path, $"Expected cloned master repo {repo} but it is missing");

                    string gitRedirectPath = Path.Join(path, ".git");
                    string expectedGitDir = Path.Join(gitDirFolder, repo);
                    string expectedRedirect = $"gitdir: {expectedGitDir}.git";
                    string actualRedirect = File.ReadAllText(gitRedirectPath);
                    Assert.AreEqual(expectedRedirect, actualRedirect, $"Expected {path} to have .gitdir redirect of {expectedRedirect}, actual {actualRedirect}");
                }

                TestContext.WriteLine("Ensure the presence of the cloned repo directories");
                string[] allRepos = Directory.GetDirectories(clonedReposFolder);
                IEnumerable<string> reposFolderNames = allRepos.Select(d => Path.GetFileName(d));
                List<string> actualRepos = reposFolderNames.Where(fn => fn.Contains(".")).ToList();
                List<string> actualMasterRepos = reposFolderNames.Where(fn => !fn.Contains(".")).ToList();

                Assert.AreEqual(expectedRepos.Count, actualRepos.Count);
                Assert.AreEqual(expectedRepos.Keys, actualRepos);

                Assert.AreEqual(expectedMasterRepos.Length, actualMasterRepos.Count);
                Assert.AreEqual(expectedMasterRepos, actualMasterRepos);

                TestContext.WriteLine("Validating the existence and content of the git directories");
                foreach (string dir in expectedGitDirs)
                {
                    string path = Path.Join(gitDirFolder, dir);
                    DirectoryAssert.Exists(path, $"Expected a .gitdir for '{dir}', but not found at {path}");
                }

                string[] actualGitDirs = Directory.GetDirectories(gitDirFolder);
                List<string> actualGitDirNames = actualGitDirs.Select(d => Path.GetFileName(d)).ToList();
                Assert.AreEqual(expectedGitDirs, actualGitDirNames);

                foreach (string gitDirectory in actualGitDirs)
                {
                    TestContext.WriteLine($"Checking content of {gitDirectory}");
                    string[] expectedFolders = { "hooks", "info", "logs", "objects", "refs" };
                    string[] actualFolders = Directory.GetDirectories(gitDirectory);
                    List<string> actualFolderNames = actualFolders.Select(d => Path.GetFileName(d)).ToList();
                    Assert.AreEqual(expectedFolders, actualFolderNames);

                    string[] expectedFiles = { "config", "description", "FETCH_HEAD", "HEAD", "index" };
                    string[] actualFiles = Directory.GetFiles(gitDirectory).Select(p => Path.GetFileName(p)).ToArray();
                    Assert.AreEqual(expectedFiles, actualFiles);
                }
            }
        }
    }
}
