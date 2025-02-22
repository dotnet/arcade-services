// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;

namespace Microsoft.DotNet.Darc.Tests;

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
    private readonly string _testName;
    private VersionDetailsParser _versionDetailsParser;
    private const string InputRootDir = "inputs";
    private const string InputDir = "input";
    private const string OutputDir = "output";

    public string TemporaryRepositoryPath { get; private set; }
    public string RootInputsPath { get => Path.Combine(Environment.CurrentDirectory, InputRootDir, _testName, InputDir); }
    public string RootExpectedOutputsPath { get => Path.Combine(Environment.CurrentDirectory, InputRootDir, _testName, OutputDir); }
    public string TemporaryRepositoryOutputsPath { get => Path.Combine(TemporaryRepositoryPath, OutputDir); }
    public LocalLibGit2Client GitClient { get; private set; }
    public DependencyFileManager DependencyFileManager { get; private set; }

    public DependencyTestDriver(string testName)
    {
        _testName = testName;
    }

    /// <summary>
    ///     Set up the test, copying inputs to the temp repo
    ///     and creating a git file manager for that repo
    /// </summary>
    public async Task Setup()
    {
        // Create the temp repo and output dirs
        TemporaryRepositoryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(TemporaryRepositoryPath);

        // Copy /rename all inputs to that temp repo
        CopyDirectoryAndRenameTestAssets(RootInputsPath, TemporaryRepositoryPath);

        // Copy /rename all outputs (if they exist) to the temp repo
        if (Directory.Exists(RootExpectedOutputsPath))
        {
            Directory.CreateDirectory(TemporaryRepositoryOutputsPath);
            CopyDirectoryAndRenameTestAssets(RootExpectedOutputsPath, TemporaryRepositoryOutputsPath);
        }

        // Set up a git file manager
        var processManager = new ProcessManager(NullLogger.Instance, "git");
        GitClient = new LocalLibGit2Client(new RemoteTokenProvider(), new NoTelemetryRecorder(), processManager, new FileSystem(), NullLogger.Instance);
        _versionDetailsParser = new VersionDetailsParser();
        DependencyFileManager = new DependencyFileManager(GitClient, _versionDetailsParser, NullLogger.Instance);

        await processManager.ExecuteGit(TemporaryRepositoryPath, ["init"]);
        await processManager.ExecuteGit(TemporaryRepositoryPath, ["config", "user.email", DarcLib.Constants.DarcBotEmail]);
        await processManager.ExecuteGit(TemporaryRepositoryPath, ["config", "user.name", DarcLib.Constants.DarcBotName]);
        await GitClient.StageAsync(TemporaryRepositoryPath, new[] { "*" });
        await GitClient.CommitAsync(TemporaryRepositoryPath, "Initial commit", allowEmpty: false, author: ((string, string)?)null);
    }

    public async Task AddDependencyAsync(DependencyDetail dependency)
    {
        await DependencyFileManager.AddDependencyAsync(
            dependency,
            TemporaryRepositoryPath,
            null);
    }

    public async Task UpdateDependenciesAsync(List<DependencyDetail> dependencies, SemanticVersion dotNetVersion = null)
    {
        GitFileContentContainer container = await DependencyFileManager.UpdateDependencyFiles(
            dependencies,
            sourceDependency: null,
            TemporaryRepositoryPath,
            null,
            null,
            dotNetVersion);
        List<GitFile> filesToUpdate = container.GetFilesToCommit();
        await GitClient.CommitFilesAsync(filesToUpdate, TemporaryRepositoryPath, null, null);
    }

    public async Task UpdatePinnedDependenciesAsync()
    {
        string testVersionDetailsXmlPath = Path.Combine(RootExpectedOutputsPath, VersionFiles.VersionDetailsXml);
        string versionDetailsContents = File.ReadAllText(testVersionDetailsXmlPath);
        IEnumerable<DependencyDetail> dependencies = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsContents, false).Dependencies;

        GitFileContentContainer container = await DependencyFileManager.UpdateDependencyFiles(
            dependencies,
            sourceDependency: null,
            TemporaryRepositoryPath,
            null,
            null,
            null);
        List<GitFile> filesToUpdate = container.GetFilesToCommit();
        await GitClient.CommitFilesAsync(filesToUpdate, TemporaryRepositoryPath, null, null);
    }

    public async Task VerifyAsync()
    {
        await DependencyFileManager.Verify(TemporaryRepositoryPath, null);
    }

    public async Task<DependencyGraph> GetDependencyGraph(string rootRepoFolder, string rootRepoCommit, bool includeToolset)
    {
        var dependencyGraphBuildOptions = new DependencyGraphBuildOptions()
        {
            IncludeToolset = includeToolset,
            LookupBuilds = false,
            NodeDiff = NodeDiff.None
        };

        return await DependencyGraph.BuildLocalDependencyGraphAsync(
            null,
            dependencyGraphBuildOptions,
            NullLogger.Instance,
            rootRepoFolder,
            rootRepoCommit,
            reposFolder: null,
            remotesMap: null,
            testPath: RootInputsPath);
    }

    private static async Task TestAndCompareImpl(
        string testInputsName,
        bool compareOutput,
        Func<DependencyTestDriver, Task> testFunc)
    {
        var dependencyTestDriver = new DependencyTestDriver(testInputsName);
        try
        {
            await dependencyTestDriver.Setup();
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

    public static Task TestAndCompareOutput(string testInputsName, Func<DependencyTestDriver, Task> testFunc)
    {
        return TestAndCompareImpl(testInputsName, true, testFunc);
    }

    public static Task TestNoCompare(string testInputsName, Func<DependencyTestDriver, Task> testFunc)
    {
        return TestAndCompareImpl(testInputsName, false, testFunc);
    }

    public static async Task GetGraphAndCompare(string testInputsName,
        Func<DependencyTestDriver, Task<DependencyGraph>> testFunc,
        string expectedGraphFile)
    {
        var dependencyTestDriver = new DependencyTestDriver(testInputsName);

        try
        {
            await dependencyTestDriver.Setup();
            DependencyGraph dependencyGraph = await testFunc(dependencyTestDriver);

            // Load in the expected graph and validate against the dependencyGraph
            var graphDocument = new XmlDocument();
            graphDocument.Load(Path.Combine(dependencyTestDriver.RootInputsPath, expectedGraphFile));

            // Compare the root node
            AssertDependencyGraphNodeIsEqual(dependencyGraph.Root, graphDocument.SelectSingleNode("//RootNode/DependencyGraphNode"));

            // Compare all the nodes
            XmlNodeList allNodes = graphDocument.SelectNodes("//AllNodes/DependencyGraphNode");
            AssetGraphNodeListIsEqual(dependencyGraph.Nodes, allNodes);

            // Compare incoherencies
            XmlNodeList incoherentNodes = graphDocument.SelectNodes("//IncoherentNodes/DependencyGraphNode");
            AssetGraphNodeListIsEqual(dependencyGraph.IncoherentNodes, incoherentNodes);

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

                matchingDependency.Should().NotBeNull();
            }
        }
        finally
        {
            dependencyTestDriver.Cleanup();
        }
    }

    private static void AssetGraphNodeListIsEqual(IEnumerable<DependencyGraphNode> nodes, XmlNodeList nodeList)
    {
        foreach (XmlNode node in nodeList)
        {
            string repoUri = node.SelectSingleNode("RepoUri").InnerText;
            string commit = node.SelectSingleNode("Commit").InnerText;
            DependencyGraphNode matchingNode = nodes.FirstOrDefault(graphNode =>
                graphNode.Repository == repoUri &&
                graphNode.Commit == commit);

            matchingNode.Should().NotBeNull();
            AssertDependencyGraphNodeIsEqual(matchingNode, node);
        }
    }

    private static void AssertDependencyGraphNodeIsEqual(DependencyGraphNode graphNode, XmlNode xmlNode)
    {
        // Check root commit info
        xmlNode.SelectSingleNode("RepoUri").InnerText.Should().Be(graphNode.Repository);
        xmlNode.SelectSingleNode("Commit").InnerText.Should().Be(graphNode.Commit);

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

            matchingDependency.Should().NotBeNull();
        }

        AssertMatchingGraphNodeReferenceList(xmlNode.SelectNodes("/Children/Child"), graphNode.Children);
        AssertMatchingGraphNodeReferenceList(xmlNode.SelectNodes("/Parents/Parent"), graphNode.Parents);
    }

    private static void AssertMatchingGraphNodeReferenceList(XmlNodeList xmlNodes, IEnumerable<DependencyGraphNode> graphNodes)
    {
        foreach (XmlNode node in xmlNodes)
        {
            string repoUri = node.SelectSingleNode("RepoUri").InnerText;
            string commit = node.SelectSingleNode("Commit").InnerText;

            var matchingNode = graphNodes.FirstOrDefault(graphNode =>
                graphNode.Repository == repoUri &&
                graphNode.Commit == commit);

            matchingNode.Should().NotBeNull();
        }
    }

    /// <summary>
    ///     Determine whether a file in the input path is the same a file in the output path.
    /// </summary>
    /// <param name="actualOutputPath">Subpath to the outputs in the temporary repo</param>
    /// <param name="expectedOutputPath">Subpath to the expected outputs</param>
    public async Task AssertEqual(string actualOutputPath, string expectedOutputPath)
    {
        string expectedOutputFilePath = Path.Combine(TemporaryRepositoryOutputsPath, expectedOutputPath);
        string actualOutputFilePath = Path.Combine(TemporaryRepositoryPath, actualOutputPath);
        using (var expectedOutputsReader = new StreamReader(expectedOutputFilePath))
        using (var actualOutputsReader = new StreamReader(actualOutputFilePath))
        {
            string expectedOutput = TestHelpers.NormalizeLineEndings(await expectedOutputsReader.ReadToEndAsync());
            string actualOutput = TestHelpers.NormalizeLineEndings(await actualOutputsReader.ReadToEndAsync());
            actualOutput.Should().Be(expectedOutput);
        }
    }

    /// <summary>
    ///     Clean temporary files
    /// </summary>
    public void Cleanup()
    {
        try
        {
            Directory.Delete(TemporaryRepositoryPath, true);
        }
        catch (DirectoryNotFoundException)
        {
            // Good, it's already gone
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    ///     Copy a directory, subdirectories and files from <paramref name="source"/> to <paramref name="destination"/>
    ///     the ".test" extension will be removed from any input files
    /// </summary>
    /// <param name="source">Source directory to copy</param>
    /// <param name="destination">Destination directory to copy</param>
    private static void CopyDirectoryAndRenameTestAssets(string source, string destination)
    {
        if (!Directory.Exists(destination))
        {
            Directory.CreateDirectory(destination);
        }

        var sourceDir = new DirectoryInfo(source);

        FileInfo[] files = sourceDir.GetFiles();
        foreach (FileInfo file in files)
        {
            file.CopyTo(Path.Combine(destination, file.Name.Replace(".test", "")), true);
        }

        DirectoryInfo[] subDirs = sourceDir.GetDirectories();
        foreach (DirectoryInfo dir in subDirs)
        {
            CopyDirectoryAndRenameTestAssets(dir.FullName, Path.Combine(destination, dir.Name));
        }
    }
}
