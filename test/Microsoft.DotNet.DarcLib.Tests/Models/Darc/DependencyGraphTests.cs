// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Models.Darc.UnitTests;

public class DependencyGraphTests
{
    /// <summary>
    /// Validates that the constructor assigns all provided parameters directly to the corresponding properties
    /// without transformation or copying.
    /// Inputs:
    ///  - A non-null root node.
    ///  - Non-null enumerables for unique dependencies, incoherent dependencies, all nodes, incoherent nodes, contributing builds, and cycles.
    /// Expected:
    ///  - Each property on the DependencyGraph instance equals (same reference) the provided parameter.
    ///  - No exceptions are thrown.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AssignsAllProperties_FromParameters()
    {
        // Arrange
        var root = new DependencyGraphNode(
            "https://repo/root",
            "sha-root",
            new List<DependencyDetail>
            {
                    new DependencyDetail { Name = "Dep.A", Version = "1.0.0", RepoUri = "https://dep/a", Commit = "sha-a" }
            },
            new HashSet<Build>());

        var uniqueDependencies = new List<DependencyDetail>
            {
                new DependencyDetail { Name = "U1", Version = "2.0.0", RepoUri = "https://u/1", Commit = "sha-u1" },
                new DependencyDetail { Name = "U1", Version = "2.0.0", RepoUri = "https://u/1", Commit = "sha-u1" } // duplicate to ensure pass-through
            };

        var incoherentDependencies = new List<DependencyDetail>
            {
                new DependencyDetail { Name = "I1", Version = "3.0.0", RepoUri = "https://i/1", Commit = "sha-i1" }
            };

        var nodeB = new DependencyGraphNode("https://repo/b", "sha-b", new List<DependencyDetail>(), new HashSet<Build>());
        var nodeC = new DependencyGraphNode("https://repo/c", "sha-c", new List<DependencyDetail>(), new HashSet<Build>());

        var allNodes = new List<DependencyGraphNode> { root, nodeB, nodeC };
        var incoherentNodes = new List<DependencyGraphNode> { nodeB, nodeC };
        var contributingBuilds = Enumerable.Empty<Build>();

        var cycles = new List<IEnumerable<DependencyGraphNode>>
            {
                new List<DependencyGraphNode> { nodeB, nodeC },
                new List<DependencyGraphNode> { nodeC, nodeB, root }
            };

        // Act
        var graph = new DependencyGraph(
            root,
            uniqueDependencies,
            incoherentDependencies,
            allNodes,
            incoherentNodes,
            contributingBuilds,
            cycles);

        // Assert
        graph.Root.Should().BeSameAs(root);
        graph.UniqueDependencies.Should().BeSameAs(uniqueDependencies);
        graph.IncoherentDependencies.Should().BeSameAs(incoherentDependencies);
        graph.Nodes.Should().BeSameAs(allNodes);
        graph.IncoherentNodes.Should().BeSameAs(incoherentNodes);
        graph.ContributingBuilds.Should().BeSameAs(contributingBuilds);
        graph.Cycles.Should().BeSameAs(cycles);
    }

    /// <summary>
    /// Ensures that null inputs are accepted by the constructor and that corresponding properties are set to null.
    /// Inputs:
    ///  - Each test case sets exactly one constructor parameter to null while providing valid non-null values for others.
    /// Expected:
    ///  - The property corresponding to the null parameter is null.
    ///  - All other properties equal (same reference) their provided non-null inputs.
    /// </summary>
    [TestCase("root")]
    [TestCase("uniqueDependencies")]
    [TestCase("incoherentDependencies")]
    [TestCase("allNodes")]
    [TestCase("incoherentNodes")]
    [TestCase("contributingBuilds")]
    [TestCase("cycles")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AllowsNullInputs_PropertiesMatchNulls(string nullParam)
    {
        // Arrange
        var root = new DependencyGraphNode("https://repo/root", "sha-root", new List<DependencyDetail>(), new HashSet<Build>());
        var uniqueDependencies = new List<DependencyDetail> { new DependencyDetail { Name = "U", Version = "1", RepoUri = "r", Commit = "c" } };
        var incoherentDependencies = new List<DependencyDetail> { new DependencyDetail { Name = "I", Version = "1", RepoUri = "r", Commit = "c" } };
        var nodeX = new DependencyGraphNode("https://repo/x", "sha-x", new List<DependencyDetail>(), new HashSet<Build>());
        var nodeY = new DependencyGraphNode("https://repo/y", "sha-y", new List<DependencyDetail>(), new HashSet<Build>());
        var allNodes = new List<DependencyGraphNode> { nodeX, nodeY };
        var incoherentNodes = new List<DependencyGraphNode> { nodeY };
        var contributingBuilds = Enumerable.Empty<Build>();
        var cycles = new List<IEnumerable<DependencyGraphNode>> { new List<DependencyGraphNode> { nodeX, nodeY } };

        if (nullParam == "root") root = null;
        if (nullParam == "uniqueDependencies") uniqueDependencies = null;
        if (nullParam == "incoherentDependencies") incoherentDependencies = null;
        if (nullParam == "allNodes") allNodes = null;
        if (nullParam == "incoherentNodes") incoherentNodes = null;
        if (nullParam == "contributingBuilds") contributingBuilds = null;
        if (nullParam == "cycles") cycles = null;

        // Act
        var graph = new DependencyGraph(
            root,
            uniqueDependencies,
            incoherentDependencies,
            allNodes,
            incoherentNodes,
            contributingBuilds,
            cycles);

        // Assert
        if (nullParam == "root") graph.Root.Should().BeNull(); else graph.Root.Should().BeSameAs(root);
        if (nullParam == "uniqueDependencies") graph.UniqueDependencies.Should().BeNull(); else graph.UniqueDependencies.Should().BeSameAs(uniqueDependencies);
        if (nullParam == "incoherentDependencies") graph.IncoherentDependencies.Should().BeNull(); else graph.IncoherentDependencies.Should().BeSameAs(incoherentDependencies);
        if (nullParam == "allNodes") graph.Nodes.Should().BeNull(); else graph.Nodes.Should().BeSameAs(allNodes);
        if (nullParam == "incoherentNodes") graph.IncoherentNodes.Should().BeNull(); else graph.IncoherentNodes.Should().BeSameAs(incoherentNodes);
        if (nullParam == "contributingBuilds") graph.ContributingBuilds.Should().BeNull(); else graph.ContributingBuilds.Should().BeSameAs(contributingBuilds);
        if (nullParam == "cycles") graph.Cycles.Should().BeNull(); else graph.Cycles.Should().BeSameAs(cycles);
    }

    /// <summary>
    /// Verifies that enumerable parameters are not copied by the constructor by mutating them after construction.
    /// Inputs:
    ///  - Lists for dependencies and nodes passed to the constructor.
    ///  - After constructing the graph, the original lists are mutated (items added).
    /// Expected:
    ///  - The graph's enumerable properties reflect the mutations (since references are preserved).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_EnumerableReferencesArePreserved_ExternalMutationsAreReflected()
    {
        // Arrange
        var root = new DependencyGraphNode("https://repo/root", "sha-root", new List<DependencyDetail>(), new HashSet<Build>());

        var uniqueDependencies = new List<DependencyDetail>();
        var incoherentDependencies = new List<DependencyDetail>();
        var allNodes = new List<DependencyGraphNode> { root };
        var incoherentNodes = new List<DependencyGraphNode>();
        var contributingBuilds = new List<Build>(); // remains empty - no Build instances required
        var cycles = new List<IEnumerable<DependencyGraphNode>> { new List<DependencyGraphNode> { root } };

        var graph = new DependencyGraph(
            root,
            uniqueDependencies,
            incoherentDependencies,
            allNodes,
            incoherentNodes,
            contributingBuilds,
            cycles);

        // Act
        uniqueDependencies.Add(new DependencyDetail { Name = "After", Version = "1", RepoUri = "r", Commit = "c" });
        incoherentDependencies.Add(new DependencyDetail { Name = "AfterI", Version = "1", RepoUri = "r", Commit = "c" });
        allNodes.Add(new DependencyGraphNode("https://repo/after", "sha-after", new List<DependencyDetail>(), new HashSet<Build>()));
        incoherentNodes.Add(root);

        // Assert
        graph.UniqueDependencies.Should().NotBeNull();
        graph.UniqueDependencies.Count().Should().Be(1);

        graph.IncoherentDependencies.Should().NotBeNull();
        graph.IncoherentDependencies.Count().Should().Be(1);

        graph.Nodes.Should().NotBeNull();
        graph.Nodes.Count().Should().Be(2);

        graph.IncoherentNodes.Should().NotBeNull();
        graph.IncoherentNodes.Count().Should().Be(1);
    }

    /// <summary>
    /// Verifies that a null IRemoteFactory causes a DarcException in remote mode (wrapper always passes remote=true).
    /// Inputs:
    ///  - remoteFactory: null
    ///  - barClient: any mock
    ///  - repoUri/commit/options/logger: valid
    /// Expected:
    ///  - DarcException with message indicating remote factory is required.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void BuildRemoteDependencyGraphAsync_NullRemoteFactory_ThrowsDarcException()
    {
        // Arrange
        IRemoteFactory remoteFactory = null;
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var options = new DependencyGraphBuildOptions();

        // Act
        Func<Task> act = () => DependencyGraph.BuildRemoteDependencyGraphAsync(
            remoteFactory,
            barClient,
            "https://repo/any",
            "sha-any",
            options,
            logger);

        // Assert
        act.Should().ThrowAsync<DarcException>()
           .WithMessage("Remote graph build requires a remote factory.");
    }

    /// <summary>
    /// Ensures that when NodeDiff is set while LookupBuilds is false (in remote mode),
    /// the underlying validation throws a DarcException.
    /// Inputs:
    ///  - options.NodeDiff: LatestInGraph
    ///  - options.LookupBuilds: false
    ///  - remoteFactory: valid mock
    /// Expected:
    ///  - DarcException with message that node diff requires build lookup.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void BuildRemoteDependencyGraphAsync_NodeDiffWithoutLookupBuilds_ThrowsDarcException()
    {
        // Arrange
        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;

        var options = new DependencyGraphBuildOptions
        {
            LookupBuilds = false,
            NodeDiff = NodeDiff.LatestInGraph
        };

        // Act
        Func<Task> act = () => DependencyGraph.BuildRemoteDependencyGraphAsync(
            remoteFactoryMock.Object,
            barClient,
            "https://repo/any",
            "sha-any",
            options,
            logger);

        // Assert
        act.Should().ThrowAsync<DarcException>()
           .WithMessage("Node diff requires build lookup.");
    }

    /// <summary>
    /// Validates that the wrapper forwards repoUri/commit to the underlying implementation which, in turn,
    /// calls IRemoteFactory.CreateRemoteAsync(repoUri) and IRemote.GetDependenciesAsync(repoUri, commit, null),
    /// and that the returned graph contains a root node with the same repoUri and commit.
    /// Inputs (parameterized):
    ///  - Various repoUri and commit values including null, empty, whitespace, and special characters.
    /// Expected:
    ///  - No exception.
    ///  - IRemoteFactory.CreateRemoteAsync invoked once with the same repoUri.
    ///  - IRemote.GetDependenciesAsync invoked once with the same repoUri and commit (name == null).
    ///  - Result.Root.Repository == repoUri and Result.Root.Commit == commit.
    ///  - Result.UniqueDependencies is empty when remote returns no dependencies.
    /// </summary>
    [TestCaseSource(nameof(RepoCommitCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task BuildRemoteDependencyGraphAsync_ParametersForwarded_ResultRootMatches(string repoUri, string commit)
    {
        // Arrange
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;

        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        remoteMock
            .Setup(r => r.GetDependenciesAsync(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(n => n == null)))
            .ReturnsAsync(new List<DependencyDetail>());

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        remoteFactoryMock
            .Setup(f => f.CreateRemoteAsync(It.IsAny<string>()))
            .ReturnsAsync(remoteMock.Object);

        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict); // No calls expected

        var options = new DependencyGraphBuildOptions
        {
            LookupBuilds = false,
            NodeDiff = NodeDiff.None,
            IncludeToolset = false
        };

        // Act
        var result = await DependencyGraph.BuildRemoteDependencyGraphAsync(
            remoteFactoryMock.Object,
            barClientMock.Object,
            repoUri,
            commit,
            options,
            logger);

        // Assert
        result.Should().NotBeNull();
        result.Root.Should().NotBeNull();
        result.Root.Repository.Should().Be(repoUri);
        result.Root.Commit.Should().Be(commit);
        result.UniqueDependencies.Should().BeEmpty();

        remoteFactoryMock.Verify(f => f.CreateRemoteAsync(repoUri), Times.Once);
        remoteMock.Verify(r => r.GetDependenciesAsync(repoUri, commit, null), Times.Once);
        barClientMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Ensures that when LookupBuilds is enabled, the BAR client is used to fetch builds for the root repo/commit.
    /// Inputs:
    ///  - options.LookupBuilds: true
    ///  - repoUri/commit: specific values
    /// Expected:
    ///  - IBasicBarClient.GetBuildsAsync(repoUri, commit) is called exactly once.
    ///  - The method completes without throwing and returns a DependencyGraph.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public async Task BuildRemoteDependencyGraphAsync_LookupBuildsTrue_CallsGetBuildsForRoot()
    {
        // Arrange
        var repoUri = "https://github.com/dotnet/example";
        var commit = "deadbeef";
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;

        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        remoteMock
            .Setup(r => r.GetDependenciesAsync(It.IsAny<string>(), It.IsAny<string>(), It.Is<string>(n => n == null)))
            .ReturnsAsync(new List<DependencyDetail>());

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        remoteFactoryMock
            .Setup(f => f.CreateRemoteAsync(It.IsAny<string>()))
            .ReturnsAsync(remoteMock.Object);

        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        barClientMock
            .Setup(b => b.GetBuildsAsync(repoUri, commit))
            .ReturnsAsync(new List<Build>());

        var options = new DependencyGraphBuildOptions
        {
            LookupBuilds = true,
            NodeDiff = NodeDiff.None
        };

        // Act
        var result = await DependencyGraph.BuildRemoteDependencyGraphAsync(
            remoteFactoryMock.Object,
            barClientMock.Object,
            repoUri,
            commit,
            options,
            logger);

        // Assert
        result.Should().NotBeNull();
        barClientMock.Verify(b => b.GetBuildsAsync(repoUri, commit), Times.Once);
        remoteFactoryMock.Verify(f => f.CreateRemoteAsync(repoUri), Times.Once);
        remoteMock.Verify(r => r.GetDependenciesAsync(repoUri, commit, null), Times.Once);
    }

    private static IEnumerable<TestCaseData> RepoCommitCases()
    {
        yield return new TestCaseData("https://github.com/dotnet/arcade", "abc123").SetName("ValidUriAndCommit");
        yield return new TestCaseData(null, null).SetName("Nulls");
        yield return new TestCaseData("", "").SetName("EmptyStrings");
        yield return new TestCaseData(" ", " ").SetName("WhitespaceStrings");
        yield return new TestCaseData("weird://uri?x=1&y=2#frag", "sha-!@#%^\\t").SetName("SpecialCharacters");
        yield return new TestCaseData(new string('a', 2048), new string('b', 1024)).SetName("VeryLongStrings");
    }

    /// <summary>
    /// Verifies that for valid local graph options (LookupBuilds == false, NodeDiff == None) and a non-empty
    /// root dependency set, the method returns a graph whose root matches the provided rootRepoFolder/rootRepoCommit,
    /// and does not expand beyond the root when dependencies lack RepoUri/Commit.
    /// Inputs:
    ///  - Different combinations of rootRepoFolder and rootRepoCommit string values (normal, empty, long, special chars).
    /// Expected:
    ///  - Returned graph is not null.
    ///  - Root.Repository == rootRepoFolder and Root.Commit == rootRepoCommit.
    ///  - Nodes collection contains only the root node.
    ///  - No incoherent nodes; cycles collection is empty.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase("repo-x", "sha-x", TestName = "BuildLocalDependencyGraphAsync_ValidOptionsWithRootDependencies_ReturnsGraphContainingOnlyRootNode_NormalStrings")]
    [TestCase("", "", TestName = "BuildLocalDependencyGraphAsync_ValidOptionsWithRootDependencies_ReturnsGraphContainingOnlyRootNode_EmptyStrings")]
    [TestCase("C:\\\\path\\\\to\\\\repo", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", TestName = "BuildLocalDependencyGraphAsync_ValidOptionsWithRootDependencies_ReturnsGraphContainingOnlyRootNode_LongCommit")]
    [TestCase("https://github.com/dotnet/😀", "sha-ßpecial\t\n", TestName = "BuildLocalDependencyGraphAsync_ValidOptionsWithRootDependencies_ReturnsGraphContainingOnlyRootNode_SpecialCharacters")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildLocalDependencyGraphAsync_ValidOptionsWithRootDependencies_ReturnsGraphContainingOnlyRootNode(string rootRepoFolder, string rootRepoCommit)
    {
        // Arrange
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var options = new DependencyGraphBuildOptions
        {
            LookupBuilds = false,
            NodeDiff = NodeDiff.None,
            IncludeToolset = false,
            ComputeCyclePaths = false
        };
        var rootDependencies = new List<DependencyDetail>
            {
                new DependencyDetail
                {
                    Name = "A",
                    Version = "1.0.0",
                    RepoUri = "",   // empty to ensure the walker skips creating child nodes
                    Commit = ""     // empty to ensure the walker skips creating child nodes
                }
            };
        var reposFolder = "repos-folder";
        var remotesMap = new List<string>(); // empty collection rather than null to respect project nullability constraints

        // Act
        var graph = await DependencyGraph.BuildLocalDependencyGraphAsync(
            rootDependencies,
            options,
            logger,
            rootRepoFolder,
            rootRepoCommit,
            reposFolder,
            remotesMap);

        // Assert
        graph.Should().NotBeNull();
        graph.Root.Should().NotBeNull();
        graph.Root.Repository.Should().Be(rootRepoFolder);
        graph.Root.Commit.Should().Be(rootRepoCommit);

        graph.Nodes.Should().NotBeNull();
        graph.Nodes.Count().Should().Be(1);

        graph.IncoherentNodes.Should().NotBeNull();
        graph.IncoherentNodes.Count().Should().Be(0);

        graph.Cycles.Should().NotBeNull();
        graph.Cycles.Count().Should().Be(0);

        graph.UniqueDependencies.Should().NotBeNull();
        graph.UniqueDependencies.Select(d => d.Name).Should().Contain("A");
    }

    /// <summary>
    /// Ensures that local graph builds reject LookupBuilds == true.
    /// Inputs:
    ///  - options.LookupBuilds = true, NodeDiff = None, non-empty rootDependencies.
    /// Expected:
    ///  - Throws DarcException with message "Build lookup only available in remote build mode."
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildLocalDependencyGraphAsync_LookupBuildsTrue_ThrowsDarcException()
    {
        // Arrange
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var options = new DependencyGraphBuildOptions
        {
            LookupBuilds = true,
            NodeDiff = NodeDiff.None
        };
        var rootDependencies = new List<DependencyDetail>
            {
                new DependencyDetail { Name = "A", Version = "1.0.0", RepoUri = "", Commit = "" }
            };

        // Act
        Func<Task> act = async () => await DependencyGraph.BuildLocalDependencyGraphAsync(
            rootDependencies,
            options,
            logger,
            "root-folder",
            "root-commit",
            "repos-folder",
            new List<string>());

        // Assert
        await act.Should().ThrowExactlyAsync<DarcException>()
            .WithMessage("Build lookup only available in remote build mode.");
    }

    /// <summary>
    /// Ensures that local graph builds reject any NodeDiff other than None.
    /// Inputs:
    ///  - options.NodeDiff = LatestInGraph or LatestInChannel, LookupBuilds = false, non-empty rootDependencies.
    /// Expected:
    ///  - Throws DarcException with appropriate message indicating NodeDiff is only available in remote mode.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(NodeDiff.LatestInGraph, "Node diff type 'LatestInGraph' only available in remote build mode.", TestName = "BuildLocalDependencyGraphAsync_NonNoneNodeDiff_ThrowsDarcException_LatestInGraph")]
    [TestCase(NodeDiff.LatestInChannel, "Node diff type 'LatestInChannel' only available in remote build mode.", TestName = "BuildLocalDependencyGraphAsync_NonNoneNodeDiff_ThrowsDarcException_LatestInChannel")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildLocalDependencyGraphAsync_NonNoneNodeDiff_ThrowsDarcException(NodeDiff nodeDiff, string expectedMessage)
    {
        // Arrange
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var options = new DependencyGraphBuildOptions
        {
            LookupBuilds = false,
            NodeDiff = nodeDiff
        };
        var rootDependencies = new List<DependencyDetail>
            {
                new DependencyDetail { Name = "A", Version = "1.0.0", RepoUri = "", Commit = "" }
            };

        // Act
        Func<Task> act = async () => await DependencyGraph.BuildLocalDependencyGraphAsync(
            rootDependencies,
            options,
            logger,
            "root-folder",
            "root-commit",
            "repos-folder",
            new List<string>());

        // Assert
        await act.Should().ThrowExactlyAsync<DarcException>()
            .WithMessage(expectedMessage);
    }

    /// <summary>
    /// Validates that passing an empty rootDependencies collection is rejected.
    /// Inputs:
    ///  - rootDependencies: empty collection.
    ///  - options: LookupBuilds = false, NodeDiff = None.
    /// Expected:
    ///  - Throws DarcException with message "Root dependencies were not supplied."
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task BuildLocalDependencyGraphAsync_EmptyRootDependencies_ThrowsDarcException()
    {
        // Arrange
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;
        var options = new DependencyGraphBuildOptions
        {
            LookupBuilds = false,
            NodeDiff = NodeDiff.None
        };
        var rootDependencies = new List<DependencyDetail>(); // empty triggers validation failure

        // Act
        Func<Task> act = async () => await DependencyGraph.BuildLocalDependencyGraphAsync(
            rootDependencies,
            options,
            logger,
            "root-folder",
            "root-commit",
            "repos-folder",
            new List<string>());

        // Assert
        await act.Should().ThrowExactlyAsync<DarcException>()
            .WithMessage("Root dependencies were not supplied.");
    }
}

public class LooseDependencyDetailComparerTests
{
    /// <summary>
    /// Verifies that LooseDependencyDetailComparer.Equals returns true only when Name, Version, and Commit match exactly
    /// (case-sensitive), and false otherwise. Also validates comparisons with null, empty, whitespace, special, and very long strings.
    /// Inputs:
    ///  - Combinations of Name/Version/Commit values for x and y (including null/empty/whitespace/special/very long).
    /// Expected:
    ///  - True only when all three fields are equal; false otherwise.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(EqualsCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Equals_VariousFieldCombinations_ReturnsExpected(string xName, string xVersion, string xCommit, string yName, string yVersion, string yCommit, bool expected)
    {
        // Arrange
        var x = new DependencyDetail { Name = xName, Version = xVersion, Commit = xCommit };
        var y = new DependencyDetail { Name = yName, Version = yVersion, Commit = yCommit };
        var comparer = new LooseDependencyDetailComparer();

        // Act
        var result = comparer.Equals(x, y);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Ensures that LooseDependencyDetailComparer.Equals throws NullReferenceException when either or both
    /// arguments are null, since the implementation dereferences properties without null checks.
    /// Inputs:
    ///  - x is null, y non-null
    ///  - x non-null, y null
    ///  - both x and y null
    /// Expected:
    ///  - NullReferenceException is thrown in all cases.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Equals_NullArguments_ThrowsNullReferenceException(bool xIsNull, bool yIsNull)
    {
        // Arrange
        var comparer = new LooseDependencyDetailComparer();
        DependencyDetail x = xIsNull ? null : new DependencyDetail { Name = "A", Version = "1.0.0", Commit = "sha-1" };
        DependencyDetail y = yIsNull ? null : new DependencyDetail { Name = "A", Version = "1.0.0", Commit = "sha-1" };

        // Act
        Action act = () => comparer.Equals(x, y);

        // Assert
        act.Should().ThrowExactly<NullReferenceException>();
    }

    private static IEnumerable<TestCaseData> EqualsCases()
    {
        // Identical, typical values
        yield return new TestCaseData("Package.A", "1.0.0", "sha-xyz", "Package.A", "1.0.0", "sha-xyz", true)
            .SetName("Equals_AllFieldsEqual_ReturnsTrue");

        // Individual field differences
        yield return new TestCaseData("Package.A", "1.0.0", "sha-1", "Package.A", "1.0.0", "sha-2", false)
            .SetName("Equals_DifferentCommit_ReturnsFalse");
        yield return new TestCaseData("Package.A", "1.0.0", "sha-1", "package.a", "1.0.0", "sha-1", false)
            .SetName("Equals_CaseDifferenceInName_ReturnsFalse");
        yield return new TestCaseData("Package.A", "1.0.0", "sha-1", "Package.A", "2.0.0", "sha-1", false)
            .SetName("Equals_DifferentVersion_ReturnsFalse");

        // Null and empty/whitespace scenarios
        yield return new TestCaseData(null, null, null, null, null, null, true)
            .SetName("Equals_AllNulls_ReturnsTrue");
        yield return new TestCaseData(null, "1.0.0", "sha-1", "Package.A", "1.0.0", "sha-1", false)
            .SetName("Equals_NullNameVsValue_ReturnsFalse");
        yield return new TestCaseData("Package.A", null, "sha-1", "Package.A", "1.0.0", "sha-1", false)
            .SetName("Equals_NullVersionVsValue_ReturnsFalse");
        yield return new TestCaseData("Package.A", "1.0.0", null, "Package.A", "1.0.0", "sha-1", false)
            .SetName("Equals_NullCommitVsValue_ReturnsFalse");
        yield return new TestCaseData("", "", "", "", "", "", true)
            .SetName("Equals_EmptyStrings_ReturnsTrue");
        yield return new TestCaseData(" ", " ", " ", " ", " ", " ", true)
            .SetName("Equals_WhitespaceStrings_ReturnsTrue");

        // Special characters and Unicode
        yield return new TestCaseData("Δpkg", "1.0.0-β", "şhå", "Δpkg", "1.0.0-β", "şhå", true)
            .SetName("Equals_SpecialCharacters_ReturnsTrue");

        // Very long strings
        var longStr = new string('a', 10000);
        yield return new TestCaseData(longStr, longStr, longStr, longStr, longStr, longStr, true)
            .SetName("Equals_VeryLongStringsEqual_ReturnsTrue");
        yield return new TestCaseData("Pkg", "1", longStr, "Pkg", "1", longStr + "b", false)
            .SetName("Equals_VeryLongCommitDifferent_ReturnsFalse");
    }

    /// <summary>
    /// Verifies that two DependencyDetail instances with identical Commit, Name, and Version values
    /// produce equal hash codes.
    /// Inputs:
    ///  - Various combinations of Commit, Name, Version including null, empty, whitespace, regular, and Unicode strings.
    /// Expected:
    ///  - GetHashCode returns the same value for both instances.
    /// </summary>
    [TestCase(null, null, null)]
    [TestCase(null, "Name", "1.0.0")]
    [TestCase("sha", null, "1.0.0")]
    [TestCase("sha", "Name", null)]
    [TestCase("", "", "")]
    [TestCase(" ", " ", " ")]
    [TestCase("sha-123", "Package.X", "1.2.3")]
    [TestCase("ßha-Ü", "Päckage.X-y_z", "1.0.0+meta")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetHashCode_EqualProperties_ProducesEqualHashCodes(string commit, string name, string version)
    {
        // Arrange
        var comparer = new LooseDependencyDetailComparer();

        var left = new DependencyDetail
        {
            Commit = commit,
            Name = name,
            Version = version
        };

        var right = new DependencyDetail
        {
            Commit = commit,
            Name = name,
            Version = version
        };

        // Act
        var leftHash = comparer.GetHashCode(left);
        var rightHash = comparer.GetHashCode(right);

        // Assert
        leftHash.Should().Be(rightHash);
    }

    /// <summary>
    /// Ensures that very long string inputs do not cause failures and yield consistent hash codes for identical values.
    /// Inputs:
    ///  - Very long Commit, Name, and Version strings (length 10,000).
    /// Expected:
    ///  - GetHashCode returns the same value for both instances.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetHashCode_VeryLongStrings_ProducesEqualHashCodes()
    {
        // Arrange
        var comparer = new LooseDependencyDetailComparer();
        var longCommit = new string('c', 10_000);
        var longName = new string('n', 10_000);
        var longVersion = new string('v', 10_000);

        var left = new DependencyDetail { Commit = longCommit, Name = longName, Version = longVersion };
        var right = new DependencyDetail { Commit = longCommit, Name = longName, Version = longVersion };

        // Act
        var leftHash = comparer.GetHashCode(left);
        var rightHash = comparer.GetHashCode(right);

        // Assert
        leftHash.Should().Be(rightHash);
    }

    /// <summary>
    /// Verifies that changing a single property among Commit, Name, and Version typically results
    /// in a different hash code, indicating that all three fields participate in hash computation.
    /// Inputs:
    ///  - Two instances with only one differing property per test case.
    /// Expected:
    ///  - Different hash codes for the two instances.
    /// Notes:
    ///  - While hash collisions are theoretically possible, these representative inputs are expected to yield different hashes.
    /// </summary>
    [TestCase("c1", "c2", "n", "n", "v", "v")]   // Commit differs
    [TestCase("c", "c", "n1", "n2", "v", "v")]   // Name differs
    [TestCase("c", "c", "n", "n", "v1", "v2")]   // Version differs
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetHashCode_SinglePropertyDiff_ProducesDifferentHashCodes(
        string commit1, string commit2, string name1, string name2, string version1, string version2)
    {
        // Arrange
        var comparer = new LooseDependencyDetailComparer();

        var left = new DependencyDetail { Commit = commit1, Name = name1, Version = version1 };
        var right = new DependencyDetail { Commit = commit2, Name = name2, Version = version2 };

        // Act
        var leftHash = comparer.GetHashCode(left);
        var rightHash = comparer.GetHashCode(right);

        // Assert
        leftHash.Should().NotBe(rightHash);
    }

    /// <summary>
    /// Ensures that passing a null DependencyDetail instance throws a NullReferenceException
    /// due to direct member access inside GetHashCode.
    /// Inputs:
    ///  - null for obj.
    /// Expected:
    ///  - NullReferenceException is thrown.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetHashCode_NullDependency_ThrowsNullReferenceException()
    {
        // Arrange
        var comparer = new LooseDependencyDetailComparer();

        // Act
        Action act = () => comparer.GetHashCode(null);

        // Assert
        act.Should().Throw<NullReferenceException>();
    }
}
