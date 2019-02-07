// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Xunit;

namespace Microsoft.DotNet.Darc.Tests
{
    /// <summary>
    ///     A driver that set and cleans up a dependency test.
    ///     Specifically, this class:
    ///     - Takes a test input folder (effectively a fake git repo)
    ///       and copies it to a temp location where it can be modified.
    ///     - Enables comparison of expected outputs.
    ///     - Cleans up after test
    /// </summary>
    internal class DependencyTestDriver
    {
        private string _testName;
        private LocalGitClient _gitClient;
        private GitFileManager _gitFileManager;
        private const string inputRootDir = "inputs";
        private const string inputDir = "input";
        private const string outputDir = "output";

        public string TemporaryRepositoryPath { get; private set; }
        public string RootInputsPath { get => Path.Combine(Environment.CurrentDirectory, inputRootDir, _testName, inputDir); }
        public string RootExpectedOutputsPath { get => Path.Combine(Environment.CurrentDirectory, inputRootDir, _testName, outputDir); }
        public LocalGitClient GitClient { get => _gitClient; }
        public GitFileManager GitFileManager { get => _gitFileManager; }

        public DependencyTestDriver(string testName)
        {
            _testName = testName;
        }

        /// <summary>
        ///     Set up the test, copying inputs to the temp repo
        ///     and creating a git file manager for that repo
        /// </summary>
        public void Setup()
        {
            // Create the temp repo dir
            TemporaryRepositoryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(TemporaryRepositoryPath);

            // Copy all inputs to that temp repo
            CopyDirectory(RootInputsPath, TemporaryRepositoryPath);

            // Set up a git file manager
            _gitClient = new LocalGitClient(NullLogger.Instance);
            _gitFileManager = new GitFileManager(GitClient, NullLogger.Instance);
        }

        public async Task AddDependencyAsync(DependencyDetail dependency)
        {
            await _gitFileManager.AddDependencyAsync(
                dependency,
                TemporaryRepositoryPath,
                null);
        }

        public async Task UpdateDependenciesAsync(List<DependencyDetail> dependencies)
        {
            GitFileContentContainer container = await _gitFileManager.UpdateDependencyFiles(
                dependencies,
                TemporaryRepositoryPath,
                null);
            List<GitFile> filesToUpdate = container.GetFilesToCommit();
            await _gitClient.CommitFilesAsync(filesToUpdate, TemporaryRepositoryPath, null, null);
        }

        public async Task UpdatePinnedDependenciesAsync()
        {
            string testVersionDetailsXmlPath = Path.Combine(RootExpectedOutputsPath, VersionFiles.VersionDetailsXml);
            string versionDetailsContents = File.ReadAllText(testVersionDetailsXmlPath);
            IEnumerable<DependencyDetail> dependencies = _gitFileManager.ParseVersionDetailsXml(versionDetailsContents, false);

            GitFileContentContainer container = await _gitFileManager.UpdateDependencyFiles(
                dependencies,
                TemporaryRepositoryPath,
                null);
            List<GitFile> filesToUpdate = container.GetFilesToCommit();
            await _gitClient.CommitFilesAsync(filesToUpdate, TemporaryRepositoryPath, null, null);
        }

        public async Task VerifyAsync()
        {
            await _gitFileManager.Verify(TemporaryRepositoryPath, null);
        }

        public async Task<DependencyGraph> GetDependencyGraph(string rootRepoFolder, string rootRepoCommit, bool includeToolset)
        {
            return await DependencyGraph.BuildLocalDependencyGraphAsync(
                null,
                includeToolset,
                NullLogger.Instance,
                rootRepoFolder,
                rootRepoCommit,
                reposFolder: null,
                remotesMap: null,
                testPath: RootInputsPath);
        }

        private async static void TestAndCompareImpl(
            string testInputsName, 
            bool compareOutput, 
            Func<DependencyTestDriver, Task> testFunc)
        {
            DependencyTestDriver dependencyTestDriver = new DependencyTestDriver(testInputsName);
            try
            {
                dependencyTestDriver.Setup();
                await testFunc(dependencyTestDriver);
                if (compareOutput)
                {
                    await dependencyTestDriver.AssertEqual(VersionFiles.VersionDetailsXml, VersionFiles.VersionDetailsXml);
                    await dependencyTestDriver.AssertEqual(VersionFiles.VersionProps, VersionFiles.VersionProps);
                    await dependencyTestDriver.AssertEqual(VersionFiles.GlobalJson, VersionFiles.GlobalJson);
                }
            }
            finally
            {
                dependencyTestDriver.Cleanup();
            }
        }

        public static void TestAndCompareOutput(string testInputsName, Func<DependencyTestDriver, Task> testFunc)
        {
            TestAndCompareImpl(testInputsName, true, testFunc);
        }

        public static void TestNoCompare(string testInputsName, Func<DependencyTestDriver, Task> testFunc)
        {
            TestAndCompareImpl(testInputsName, false, testFunc);
        }

        public async static void GetGraphAndCompare(string testInputsName, 
            Func<DependencyTestDriver, Task<DependencyGraph>> testFunc,
            string expectedGraphFile)
        {
            DependencyTestDriver dependencyTestDriver = new DependencyTestDriver(testInputsName);

            try
            {
                dependencyTestDriver.Setup();
                DependencyGraph dependencyGraph = await testFunc(dependencyTestDriver);

                // Load in the expected graph and validate against the dependencyGraph
                XmlDocument graphDocument = new XmlDocument();
                graphDocument.Load(Path.Combine(dependencyTestDriver.RootInputsPath, expectedGraphFile));

                // Compare the root node
                dependencyTestDriver.AssertDependencyGraphNodeIsEqual(dependencyGraph.Root, graphDocument.SelectSingleNode("//RootNode/DependencyGraphNode"));

                // Compare all the nodes
                XmlNodeList allNodes = graphDocument.SelectNodes("//AllNodes/DependencyGraphNode");
                dependencyTestDriver.AssetGraphNodeListIsEqual(dependencyGraph.Nodes, allNodes);

                // Compare incoherencies
                XmlNodeList incoherentNodes = graphDocument.SelectNodes("//IncoherentNodes/DependencyGraphNode");
                dependencyTestDriver.AssetGraphNodeListIsEqual(dependencyGraph.IncoherentNodes, incoherentNodes);

                // Compare unique dependencies
                XmlNodeList dependencyNodes = graphDocument.SelectNodes("//UniqueDependencies/Dependency");
                foreach (XmlNode dep in dependencyNodes)
                {
                    // Find each dependency in the graphNode's dependency
                    var matchingDependency = dependencyGraph.UniqueDependencies.FirstOrDefault(dependency =>
                    {
                        return (dependency.Name == dep.SelectSingleNode("Name").InnerText &&
                            dependency.Version == dep.SelectSingleNode("Version").InnerText &&
                            dependency.RepoUri == dep.SelectSingleNode("RepoUri").InnerText &&
                            dependency.Commit == dep.SelectSingleNode("Commit").InnerText &&
                            dependency.Type.ToString() == dep.SelectSingleNode("Type").InnerText);
                    });

                    Assert.NotNull(matchingDependency);
                }
            }
            finally
            {
                dependencyTestDriver.Cleanup();
            }
        }

        private void AssetGraphNodeListIsEqual(IEnumerable<DependencyGraphNode> nodes, XmlNodeList nodeList)
        {
            foreach (XmlNode node in nodeList)
            {
                string repoUri = node.SelectSingleNode("RepoUri").InnerText;
                string commit = node.SelectSingleNode("Commit").InnerText;
                DependencyGraphNode matchingNode = nodes.FirstOrDefault(graphNode =>
                        graphNode.RepoUri == repoUri &&
                        graphNode.Commit == commit);

                Assert.NotNull(matchingNode);
                AssertDependencyGraphNodeIsEqual(matchingNode, node);
            }
        }

        private void AssertDependencyGraphNodeIsEqual(DependencyGraphNode graphNode, XmlNode xmlNode)
        {
            // Check root commit info
            Assert.Equal(graphNode.RepoUri, xmlNode.SelectSingleNode("RepoUri").InnerText);
            Assert.Equal(graphNode.Commit, xmlNode.SelectSingleNode("Commit").InnerText);

            // Check dependencies
            XmlNodeList dependencyNodes = xmlNode.SelectNodes("Dependencies/Dependency");
            foreach (XmlNode dep in dependencyNodes)
            {
                string name = dep.SelectSingleNode("Name").InnerText;
                string version = dep.SelectSingleNode("Version").InnerText;
                string repoUri = dep.SelectSingleNode("RepoUri").InnerText;
                string commit = dep.SelectSingleNode("Commit").InnerText;
                string type = dep.SelectSingleNode("Type").InnerText;

                // Find each dependency in the graphNode's dependency
                var matchingDependency = graphNode.Dependencies.FirstOrDefault(dependency =>
                {
                    return (dependency.Name == name &&
                        dependency.Version == version &&
                        dependency.RepoUri == repoUri &&
                        dependency.Commit == commit &&
                        dependency.Type.ToString() == type);
                });

                Assert.NotNull(matchingDependency);
            }

            AssertMatchingGraphNodeReferenceList(xmlNode.SelectNodes("/Children/Child"), graphNode.Children);
            AssertMatchingGraphNodeReferenceList(xmlNode.SelectNodes("/Parents/Parent"), graphNode.Parents);
        }

        private void AssertMatchingGraphNodeReferenceList(XmlNodeList xmlNodes, IEnumerable<DependencyGraphNode> graphNodes)
        {   
            foreach (XmlNode node in xmlNodes)
            {
                string repoUri = node.SelectSingleNode("RepoUri").InnerText;
                string commit = node.SelectSingleNode("Commit").InnerText;

                var matchingNode = graphNodes.FirstOrDefault(graphNode =>
                    graphNode.RepoUri == repoUri &&
                    graphNode.Commit == commit);

                Assert.NotNull(matchingNode);
            }
        }

        /// <summary>
        ///     Determine whether a file in the input path is the same a file in the output path.
        /// </summary>
        /// <param name="actualOutputPath">Subpath to the outputs in the temporary repo</param>
        /// <param name="expectedOutputPath">Subpath to the expected outputs</param>
        public async Task AssertEqual(string actualOutputPath, string expectedOutputPath)
        {
            string expectedOutputFilePath = Path.Combine(RootExpectedOutputsPath, expectedOutputPath);
            string actualOutputFilePath = Path.Combine(TemporaryRepositoryPath, actualOutputPath);
            using (StreamReader expectedOutputsReader = new StreamReader(expectedOutputFilePath))
            using (StreamReader actualOutputsReader = new StreamReader(actualOutputFilePath))
            {
                string expectedOutput = await expectedOutputsReader.ReadToEndAsync();
                string actualOutput = await actualOutputsReader.ReadToEndAsync();
                Assert.Equal(
                    expectedOutput,
                    actualOutput);
            }
        }

        /// <summary>
        ///     Clean temporary files
        /// </summary>
        public void Cleanup()
        {
            Directory.Delete(TemporaryRepositoryPath, true);
        }

        /// <summary>
        ///     Copy a directory, subdirectories and files from <paramref name="source"/> to <paramref name="destination"/>
        /// </summary>
        /// <param name="source">Source directory to copy</param>
        /// <param name="destination">Destination directory to copy</param>
        private void CopyDirectory(string source, string destination)
        {
            if (!Directory.Exists(destination))
            {
                Directory.CreateDirectory(destination);
            }

            DirectoryInfo sourceDir = new DirectoryInfo(source);

            FileInfo[] files = sourceDir.GetFiles();
            foreach (FileInfo file in files)
            {
                file.CopyTo(Path.Combine(destination, file.Name), true);
            }

            DirectoryInfo[] subDirs = sourceDir.GetDirectories();
            foreach (DirectoryInfo dir in subDirs)
            {
                CopyDirectory(dir.FullName, Path.Combine(destination, dir.Name));
            }
        }
    }
}
