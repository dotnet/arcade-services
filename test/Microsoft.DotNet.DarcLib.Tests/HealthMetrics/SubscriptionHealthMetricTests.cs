// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.HealthMetrics;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.HealthMetrics.UnitTests;

public class SubscriptionHealthMetricTests
{
    /// <summary>
    /// Verifies that the constructor assigns Repository and Branch exactly as provided (including null/empty/special/long values),
    /// stores the provided dependency selector instance, and initializes default observable properties.
    /// Inputs:
    ///  - Various repo and branch string combinations, including null, empty, whitespace, special characters, Unicode, and long strings.
    /// Expected:
    ///  - Repository and Branch fields equal the inputs.
    ///  - DependencySelector is the same instance that was passed in.
    ///  - ConflictingSubscriptions, DependenciesMissingSubscriptions, DependenciesThatDoNotFlow, UnusedSubscriptions are non-null and empty.
    ///  - Subscriptions and Dependencies are null (not initialized by the constructor).
    ///  - MissingVersionDetailsFile is false.
    ///  - MetricName and MetricDescription reflect the assigned repo/branch values.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ConstructorStringCases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AssignsFieldsAndInitializesDefaults_WithVariousRepoAndBranch(string repo, string branch)
    {
        // Arrange
        var selector = new Func<DependencyDetail, bool>(_ => true);
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict).Object;
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;

        // Act
        var metric = new SubscriptionHealthMetric(repo, branch, selector, remoteFactory, barClient, logger);

        // Assert
        metric.Repository.Should().Be(repo);
        metric.Branch.Should().Be(branch);
        metric.DependencySelector.Should().BeSameAs(selector);

        metric.ConflictingSubscriptions.Should().NotBeNull();
        metric.ConflictingSubscriptions.Should().BeEmpty();

        metric.DependenciesMissingSubscriptions.Should().NotBeNull();
        metric.DependenciesMissingSubscriptions.Should().BeEmpty();

        metric.DependenciesThatDoNotFlow.Should().NotBeNull();
        metric.DependenciesThatDoNotFlow.Should().BeEmpty();

        metric.UnusedSubscriptions.Should().NotBeNull();
        metric.UnusedSubscriptions.Should().BeEmpty();

        metric.Subscriptions.Should().BeNull();
        metric.Dependencies.Should().BeNull();

        metric.MissingVersionDetailsFile.Should().BeFalse();

        metric.MetricName.Should().Be("Subscription Health");
        metric.MetricDescription.Should().Be($"Subscription health for {repo} @ {branch}");
    }

    /// <summary>
    /// Ensures that the constructor accepts null for all parameters without throwing and assigns null to corresponding fields,
    /// while default observable properties remain correctly initialized.
    /// Inputs:
    ///  - All parameters as null: repo, branch, dependencySelector, remoteFactory, barClient, logger.
    /// Expected:
    ///  - Repository, Branch, and DependencySelector are null.
    ///  - ConflictingSubscriptions, DependenciesMissingSubscriptions, DependenciesThatDoNotFlow, UnusedSubscriptions are non-null and empty.
    ///  - Subscriptions and Dependencies are null.
    ///  - MissingVersionDetailsFile is false.
    ///  - MetricDescription formats null as empty strings.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AllowsNullParameters_FieldsSetToNullAndDefaults()
    {
        // Arrange
        string repo = null;
        string branch = null;
        Func<DependencyDetail, bool> selector = null;
        IRemoteFactory remoteFactory = null;
        IBasicBarClient barClient = null;
        ILogger logger = null;

        // Act
        var metric = new SubscriptionHealthMetric(repo, branch, selector, remoteFactory, barClient, logger);

        // Assert
        metric.Repository.Should().BeNull();
        metric.Branch.Should().BeNull();
        metric.DependencySelector.Should().BeNull();

        metric.ConflictingSubscriptions.Should().NotBeNull();
        metric.ConflictingSubscriptions.Should().BeEmpty();

        metric.DependenciesMissingSubscriptions.Should().NotBeNull();
        metric.DependenciesMissingSubscriptions.Should().BeEmpty();

        metric.DependenciesThatDoNotFlow.Should().NotBeNull();
        metric.DependenciesThatDoNotFlow.Should().BeEmpty();

        metric.UnusedSubscriptions.Should().NotBeNull();
        metric.UnusedSubscriptions.Should().BeEmpty();

        metric.Subscriptions.Should().BeNull();
        metric.Dependencies.Should().BeNull();

        metric.MissingVersionDetailsFile.Should().BeFalse();

        metric.MetricName.Should().Be("Subscription Health");
        metric.MetricDescription.Should().Be("Subscription health for  @ ");
    }

    /// <summary>
    /// Validates that a provided dependency selector delegate is stored and invokable with consistent results.
    /// Inputs:
    ///  - A selector delegate that returns true for dependencies named 'Match' and false otherwise.
    ///  - Two DependencyDetail instances: one matching, one not matching.
    /// Expected:
    ///  - DependencySelector reference equals the provided delegate.
    ///  - Invoking the selector returns true for the matching dependency and false for the non-matching dependency.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AssignsDependencySelector_DelegateInvokesAsProvided()
    {
        // Arrange
        var selector = new Func<DependencyDetail, bool>(d => d != null && d.Name == "Match");
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict).Object;
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict).Object;
        var logger = new Mock<ILogger>(MockBehavior.Loose).Object;

        var matching = new DependencyDetail { Name = "Match" };
        var nonMatching = new DependencyDetail { Name = "Nope" };

        // Act
        var metric = new SubscriptionHealthMetric("repo", "branch", selector, remoteFactory, barClient, logger);

        // Assert
        metric.DependencySelector.Should().BeSameAs(selector);
        metric.DependencySelector(matching).Should().BeTrue();
        metric.DependencySelector(nonMatching).Should().BeFalse();
    }

    private static IEnumerable<TestCaseData> ConstructorStringCases()
    {
        yield return new TestCaseData(null, null).SetName("NullRepo_NullBranch");
        yield return new TestCaseData(string.Empty, string.Empty).SetName("EmptyRepo_EmptyBranch");
        yield return new TestCaseData(" ", "\t ").SetName("WhitespaceRepo_WhitespaceBranch");
        yield return new TestCaseData("https://example.com/repo.git", "refs/heads/main").SetName("UrlRepo_RefsHeadsMain");
        yield return new TestCaseData(new string('a', 1024), new string('b', 2048)).SetName("LongRepo_LongBranch");
        yield return new TestCaseData("répø🚀", "分支/特性").SetName("UnicodeRepo_UnicodeBranch");
        yield return new TestCaseData("/path/with:special?*chars", "feature/with|invalid*chars").SetName("SpecialCharsRepo_SpecialCharsBranch");
    }

    /// <summary>
    /// Verifies that MetricName returns the constant string "Subscription Health" regardless of constructor inputs.
    /// Inputs vary the repo and branch strings across edge-like values (empty, whitespace, typical, long, special).
    /// Expected result: the property equals exactly "Subscription Health".
    /// </summary>
    /// <param name="repo">Repository string input.</param>
    /// <param name="branch">Branch string input.</param>
    [Test]
    [TestCaseSource(nameof(MetricName_ReturnsSubscriptionHealth_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MetricName_AnyInputs_ReturnsSubscriptionHealth(string repo, string branch)
    {
        // Arrange
        var remoteFactory = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var barClient = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var logger = new Mock<ILogger>(MockBehavior.Loose);
        Func<DependencyDetail, bool> dependencySelector = _ => true;

        var metric = new SubscriptionHealthMetric(
            repo,
            branch,
            dependencySelector,
            remoteFactory.Object,
            barClient.Object,
            logger.Object);

        // Act
        var name = metric.MetricName;

        // Assert
        name.Should().Be("Subscription Health");
    }

    private static IEnumerable<TestCaseData> MetricName_ReturnsSubscriptionHealth_Cases()
    {
        // Repo variations
        var repos = new[]
        {
                "repo",
                "",
                " ",
                "owner/repo",
                "r",
                new string('r', 256),
                "repo-with-specials-!@#$%^&*()_+{}|:\"<>?`~[]\\;',./"
            };

        // Branch variations
        var branches = new[]
        {
                "main",
                "",
                " ",
                "develop",
                "feature/awesome-change",
                new string('b', 512),
                "refs/heads/release/1.0"
            };

        foreach (var r in repos)
        {
            foreach (var b in branches)
            {
                yield return new TestCaseData(r, b)
                    .SetName($"MetricName_AnyInputs_ReturnsSubscriptionHealth(repo=\"{Shorten(r)}\", branch=\"{Shorten(b)}\")");
            }
        }

        static string Shorten(string s)
        {
            if (s == null) return "null";
            if (s.Length <= 20) return s.Replace("\n", "\\n").Replace("\r", "\\r");
            return s.Substring(0, 20) + "...";
        }
    }

    /// <summary>
    /// Validates that MetricDescription formats the description string correctly for a variety of repository and branch inputs,
    /// including empty, whitespace-only, unicode/special characters, and very long strings.
    /// Inputs:
    /// - repo and branch strings covering edge cases.
    /// Expected:
    /// - The returned string equals "Subscription health for {repo} @ {branch}" with exact spacing and characters.
    /// </summary>
    [TestCaseSource(nameof(MetricDescription_TestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MetricDescription_VariousRepoAndBranchValues_FormatsCorrectly(string repo, string branch)
    {
        // Arrange
        var remoteFactoryMock = new Moq.Mock<IRemoteFactory>(MockBehavior.Strict);
        var barClientMock = new Moq.Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Moq.Mock<ILogger>(MockBehavior.Strict);
        Func<DependencyDetail, bool> selector = _ => true;
        var sut = new SubscriptionHealthMetric(repo, branch, selector, remoteFactoryMock.Object, barClientMock.Object, loggerMock.Object);

        // Act
        var description = sut.MetricDescription;

        // Assert
        description.Should().Be("Subscription health for " + repo + " @ " + branch);
    }

    private static IEnumerable MetricDescription_TestCases()
    {
        yield return new TestCaseData("dotnet/arcade", "main")
            .SetName("MetricDescription_NormalInputs_FormatsCorrectly");

        yield return new TestCaseData(string.Empty, string.Empty)
            .SetName("MetricDescription_EmptyStrings_FormatsCorrectly");

        yield return new TestCaseData(" ", " ")
            .SetName("MetricDescription_WhitespaceOnly_FormatsCorrectly");

        yield return new TestCaseData("🔥repo🔥", "feature/Ω-line\nnext")
            .SetName("MetricDescription_UnicodeAndControlChars_FormatsCorrectly");

        yield return new TestCaseData(new string('r', 1024), new string('b', 1024))
            .SetName("MetricDescription_VeryLongStrings_FormatsCorrectly");
    }

    /// <summary>
    /// When the dependency file is missing and there are subscriptions targeting the repo/branch,
    /// EvaluateAsync should mark MissingVersionDetailsFile = true and set Result = Failed.
    /// Inputs:
    ///  - BAR client returns one subscription that targets the specified branch.
    ///  - Remote throws DependencyFileNotFoundException.
    /// Expected:
    ///  - Result == Failed.
    ///  - MissingVersionDetailsFile == true.
    ///  - No calls to GetLatestBuildAsync occur.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task EvaluateAsync_DependencyFileMissingWithSubscriptions_FailsAndFlagsMissingDetails()
    {
        // Arrange
        var repo = "https://repo/x";
        var branch = "main";

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock
            .Setup(m => m.CreateRemoteAsync(repo))
            .ReturnsAsync(remoteMock.Object);

        var channel = new Channel(1, "name", "class");
        var sub = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo1", targetRepository: repo, targetBranch: branch, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channel,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryBuild)
        };

        barClientMock
            .Setup(m => m.GetSubscriptionsAsync(null, repo, null))
            .ReturnsAsync(new List<Subscription> { sub });

        remoteMock
            .Setup(m => m.GetDependenciesAsync(repo, branch, null))
            .ThrowsAsync(new DependencyFileNotFoundException());

        var sut = new SubscriptionHealthMetric(repo, branch, d => true, remoteFactoryMock.Object, barClientMock.Object, loggerMock.Object);

        // Act
        await sut.EvaluateAsync();

        // Assert
        sut.MissingVersionDetailsFile.Should().BeTrue();
        sut.Result.Should().Be(HealthResult.Failed);
        barClientMock.Verify(m => m.GetLatestBuildAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    /// <summary>
    /// When the dependency file is missing and no subscriptions target the repo/branch,
    /// EvaluateAsync should pass.
    /// Inputs:
    ///  - BAR client returns subscriptions that do not target the specified branch
    ///    (filtered out by case-insensitive comparison).
    ///  - Remote throws DependencyFileNotFoundException.
    /// Expected:
    ///  - Result == Passed.
    ///  - MissingVersionDetailsFile == false.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task EvaluateAsync_DependencyFileMissingWithoutSubscriptions_Passes()
    {
        // Arrange
        var repo = "https://repo/x";
        var branch = "main";

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock
            .Setup(m => m.CreateRemoteAsync(repo))
            .ReturnsAsync(remoteMock.Object);

        var channel = new Channel(2, "name", "class");
        // TargetBranch != branch -> filtered out
        var subDifferentBranch = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo1", targetRepository: repo, targetBranch: "develop", sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channel,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryBuild)
        };

        barClientMock
            .Setup(m => m.GetSubscriptionsAsync(null, repo, null))
            .ReturnsAsync(new List<Subscription> { subDifferentBranch });

        remoteMock
            .Setup(m => m.GetDependenciesAsync(repo, branch, null))
            .ThrowsAsync(new DependencyFileNotFoundException());

        var sut = new SubscriptionHealthMetric(repo, branch, d => true, remoteFactoryMock.Object, barClientMock.Object, loggerMock.Object);

        // Act
        await sut.EvaluateAsync();

        // Assert
        sut.MissingVersionDetailsFile.Should().BeFalse();
        sut.Result.Should().Be(HealthResult.Passed);
    }

    /// <summary>
    /// Validates that when dependencies are fully covered by flowing subscriptions (case-insensitive branch filter),
    /// EvaluateAsync results in Passed with no warnings or errors.
    /// Inputs:
    ///  - One subscription targeting branch with different casing and producing asset for the dependency.
    ///  - Another subscription targeting a different branch (filtered out).
    ///  - Dependencies include one valid, one pinned, and one coherent (filtered out).
    /// Expected:
    ///  - Result == Passed.
    ///  - No missing subscriptions, no conflicts, no non-flowing dependencies, and no unused subscriptions.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task EvaluateAsync_AllDependenciesCovered_NoWarningsOrErrors_Passes()
    {
        // Arrange
        var repo = "https://repo/a";
        var branch = "main";

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock.Setup(m => m.CreateRemoteAsync(repo)).ReturnsAsync(remoteMock.Object);

        var channelIncluded = new Channel(10, "included", "class");
        var subIncluded = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo1", targetRepository: repo, targetBranch: "MAIN", sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channelIncluded,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryBuild)
        };

        var channelExcluded = new Channel(11, "excluded", "class");
        var subExcludedBranch = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo2", targetRepository: repo, targetBranch: "feature/x", sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channelExcluded,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryDay)
        };

        barClientMock
            .Setup(m => m.GetSubscriptionsAsync(null, repo, null))
            .ReturnsAsync(new List<Subscription> { subIncluded, subExcludedBranch });

        var depCovered = new DependencyDetail { Name = "PkgA", Pinned = false, CoherentParentDependencyName = null };
        var depPinned = new DependencyDetail { Name = "PkgPinned", Pinned = true, CoherentParentDependencyName = null };
        var depCoherent = new DependencyDetail { Name = "PkgCoherent", Pinned = false, CoherentParentDependencyName = "Parent" };

        remoteMock
            .Setup(m => m.GetDependenciesAsync(repo, branch, null))
            .ReturnsAsync(new List<DependencyDetail> { depCovered, depPinned, depCoherent });

        var buildIncluded = NewBuildWithAssets(100, new List<Asset>
            {
                new Asset(id: 1, buildId: 100, nonShipping: false, name: "PkgA", version: "1.0.0", locations: new List<AssetLocation>())
            });

        barClientMock
            .Setup(m => m.GetLatestBuildAsync(subIncluded.SourceRepository, subIncluded.Channel.Id))
            .ReturnsAsync(buildIncluded);

        var sut = new SubscriptionHealthMetric(repo, branch, d => true, remoteFactoryMock.Object, barClientMock.Object, loggerMock.Object);

        // Act
        await sut.EvaluateAsync();

        // Assert
        sut.Result.Should().Be(HealthResult.Passed);
        sut.Subscriptions.Count.Should().Be(1);
        sut.Subscriptions.Single().Should().Be(subIncluded);
        sut.DependenciesMissingSubscriptions.Any().Should().BeFalse();
        sut.ConflictingSubscriptions.Any().Should().BeFalse();
        sut.DependenciesThatDoNotFlow.Any().Should().BeFalse();
        sut.UnusedSubscriptions.Any().Should().BeFalse();
    }

    /// <summary>
    /// Ensures that when a valid subscription exists but is not used by any dependency,
    /// EvaluateAsync results in Warning due to an unused subscription.
    /// Inputs:
    ///  - Two subscriptions targeting the branch.
    ///  - Latest build for S1 produces the dependency's asset; S2 produces an unrelated asset.
    ///  - Dependency matches S1.
    /// Expected:
    ///  - Result == Warning.
    ///  - UnusedSubscriptions contains S2.
    ///  - No missing subscriptions, no conflicts, and no non-flowing dependencies.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task EvaluateAsync_UnusedSubscription_Warns()
    {
        // Arrange
        var repo = "https://repo/b";
        var branch = "release";

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock.Setup(m => m.CreateRemoteAsync(repo)).ReturnsAsync(remoteMock.Object);

        var channel1 = new Channel(21, "c1", "class");
        var channel2 = new Channel(22, "c2", "class");

        var s1 = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo1", targetRepository: repo, targetBranch: branch, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channel1,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryBuild)
        };
        var s2 = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo2", targetRepository: repo, targetBranch: branch, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channel2,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryDay)
        };

        barClientMock
            .Setup(m => m.GetSubscriptionsAsync(null, repo, null))
            .ReturnsAsync(new List<Subscription> { s1, s2 });

        var dep = new DependencyDetail { Name = "PkgA", Pinned = false, CoherentParentDependencyName = null };
        remoteMock.Setup(m => m.GetDependenciesAsync(repo, branch, null)).ReturnsAsync(new List<DependencyDetail> { dep });

        var build1 = NewBuildWithAssets(201, new List<Asset>
            {
                new Asset(id: 2, buildId: 201, nonShipping: false, name: "PkgA", version: "1.0.0", locations: new List<AssetLocation>())
            });
        var build2 = NewBuildWithAssets(202, new List<Asset>
            {
                new Asset(id: 3, buildId: 202, nonShipping: false, name: "Unrelated", version: "1.0.0", locations: new List<AssetLocation>())
            });

        barClientMock.Setup(m => m.GetLatestBuildAsync(s1.SourceRepository, s1.Channel.Id)).ReturnsAsync(build1);
        barClientMock.Setup(m => m.GetLatestBuildAsync(s2.SourceRepository, s2.Channel.Id)).ReturnsAsync(build2);

        var sut = new SubscriptionHealthMetric(repo, branch, d => true, remoteFactoryMock.Object, barClientMock.Object, loggerMock.Object);

        // Act
        await sut.EvaluateAsync();

        // Assert
        sut.Result.Should().Be(HealthResult.Warning);
        sut.UnusedSubscriptions.Should().Contain(s2);
        sut.DependenciesMissingSubscriptions.Any().Should().BeFalse();
        sut.ConflictingSubscriptions.Any().Should().BeFalse();
        sut.DependenciesThatDoNotFlow.Any().Should().BeFalse();
    }

    /// <summary>
    /// Ensures that when a dependency is covered by a subscription that does not flow (policy 'None'),
    /// EvaluateAsync results in Warning due to non-flowing dependency.
    /// Inputs:
    ///  - One subscription with Policy.UpdateFrequency == None.
    ///  - Latest build produces the dependency's asset.
    /// Expected:
    ///  - Result == Warning.
    ///  - DependenciesThatDoNotFlow contains the dependency.
    ///  - No missing subscriptions, no conflicts, and no unused subscriptions.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task EvaluateAsync_SubscriptionDoesNotFlow_Warns()
    {
        // Arrange
        var repo = "https://repo/c";
        var branch = "main";

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock.Setup(m => m.CreateRemoteAsync(repo)).ReturnsAsync(remoteMock.Object);

        var channel = new Channel(31, "c", "class");
        var s = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo", targetRepository: repo, targetBranch: branch, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channel,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.None)
        };

        barClientMock
            .Setup(m => m.GetSubscriptionsAsync(null, repo, null))
            .ReturnsAsync(new List<Subscription> { s });

        var dep = new DependencyDetail { Name = "PkgX", Pinned = false, CoherentParentDependencyName = null };
        remoteMock.Setup(m => m.GetDependenciesAsync(repo, branch, null)).ReturnsAsync(new List<DependencyDetail> { dep });

        var build = NewBuildWithAssets(301, new List<Asset>
            {
                new Asset(id: 5, buildId: 301, nonShipping: false, name: "PkgX", version: "2.0.0", locations: new List<AssetLocation>())
            });

        barClientMock.Setup(m => m.GetLatestBuildAsync(s.SourceRepository, s.Channel.Id)).ReturnsAsync(build);

        var sut = new SubscriptionHealthMetric(repo, branch, d => true, remoteFactoryMock.Object, barClientMock.Object, loggerMock.Object);

        // Act
        await sut.EvaluateAsync();

        // Assert
        sut.Result.Should().Be(HealthResult.Warning);
        sut.DependenciesThatDoNotFlow.Should().Contain(dep);
        sut.DependenciesMissingSubscriptions.Any().Should().BeFalse();
        sut.ConflictingSubscriptions.Any().Should().BeFalse();
        sut.UnusedSubscriptions.Any().Should().BeFalse();
    }

    /// <summary>
    /// Verifies that when two subscriptions from different channels produce the same asset name,
    /// a conflict is recorded and EvaluateAsync fails regardless of dependency presence.
    /// Inputs:
    ///  - Two subscriptions targeting the same branch with different Channel.Id values.
    ///  - Both latest builds produce the same asset name.
    ///  - No dependencies.
    /// Expected:
    ///  - Result == Failed.
    ///  - ConflictingSubscriptions contains the asset conflict.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task EvaluateAsync_ConflictingSubscriptions_Fails()
    {
        // Arrange
        var repo = "https://repo/d";
        var branch = "main";

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock.Setup(m => m.CreateRemoteAsync(repo)).ReturnsAsync(remoteMock.Object);

        var channel1 = new Channel(41, "c1", "class");
        var channel2 = new Channel(42, "c2", "class");

        var s1 = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo1", targetRepository: repo, targetBranch: branch, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channel1,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryBuild)
        };
        var s2 = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo2", targetRepository: repo, targetBranch: branch, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channel2,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryBuild)
        };

        barClientMock
            .Setup(m => m.GetSubscriptionsAsync(null, repo, null))
            .ReturnsAsync(new List<Subscription> { s1, s2 });

        // No dependencies
        remoteMock.Setup(m => m.GetDependenciesAsync(repo, branch, null)).ReturnsAsync(new List<DependencyDetail>());

        var build1 = NewBuildWithAssets(401, new List<Asset> { new Asset(7, 401, false, "PkgConflict", "1.0.0", new List<AssetLocation>()) });
        var build2 = NewBuildWithAssets(402, new List<Asset> { new Asset(8, 402, false, "PkgConflict", "2.0.0", new List<AssetLocation>()) });

        barClientMock.Setup(m => m.GetLatestBuildAsync(s1.SourceRepository, s1.Channel.Id)).ReturnsAsync(build1);
        barClientMock.Setup(m => m.GetLatestBuildAsync(s2.SourceRepository, s2.Channel.Id)).ReturnsAsync(build2);

        var sut = new SubscriptionHealthMetric(repo, branch, d => true, remoteFactoryMock.Object, barClientMock.Object, loggerMock.Object);

        // Act
        await sut.EvaluateAsync();

        // Assert
        sut.Result.Should().Be(HealthResult.Failed);
        sut.ConflictingSubscriptions.Any().Should().BeTrue();
        sut.ConflictingSubscriptions.Single().Asset.Should().Be("PkgConflict");
    }

    /// <summary>
    /// Ensures that when a dependency is not covered by any subscription-produced asset,
    /// EvaluateAsync fails due to missing subscriptions.
    /// Inputs:
    ///  - One flowing subscription whose latest build does not produce the dependency's asset.
    ///  - One dependency not pinned and without a coherent parent.
    /// Expected:
    ///  - Result == Failed.
    ///  - DependenciesMissingSubscriptions contains the dependency.
    ///  - No conflicts.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public async Task EvaluateAsync_DependencyMissingSubscription_Fails()
    {
        // Arrange
        var repo = "https://repo/e";
        var branch = "main";

        var remoteFactoryMock = new Mock<IRemoteFactory>(MockBehavior.Strict);
        var remoteMock = new Mock<IRemote>(MockBehavior.Strict);
        var barClientMock = new Mock<IBasicBarClient>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger>(MockBehavior.Loose);

        remoteFactoryMock.Setup(m => m.CreateRemoteAsync(repo)).ReturnsAsync(remoteMock.Object);

        var channel = new Channel(51, "c", "class");
        var s = new Subscription(Guid.NewGuid(), enabled: true, sourceEnabled: true, sourceRepository: "https://src/repo", targetRepository: repo, targetBranch: branch, sourceDirectory: "", targetDirectory: "", pullRequestFailureNotificationTags: "", excludedAssets: new List<string>())
        {
            Channel = channel,
            Policy = new SubscriptionPolicy(batchable: true, updateFrequency: UpdateFrequency.EveryDay)
        };

        barClientMock
            .Setup(m => m.GetSubscriptionsAsync(null, repo, null))
            .ReturnsAsync(new List<Subscription> { s });

        var depMissing = new DependencyDetail { Name = "PkgMissing", Pinned = false, CoherentParentDependencyName = null };
        remoteMock.Setup(m => m.GetDependenciesAsync(repo, branch, null)).ReturnsAsync(new List<DependencyDetail> { depMissing });

        // Subscription produces a different asset
        var build = NewBuildWithAssets(501, new List<Asset>
            {
                new Asset(id: 9, buildId: 501, nonShipping: false, name: "OtherAsset", version: "1.0.0", locations: new List<AssetLocation>())
            });
        barClientMock.Setup(m => m.GetLatestBuildAsync(s.SourceRepository, s.Channel.Id)).ReturnsAsync(build);

        var sut = new SubscriptionHealthMetric(repo, branch, d => true, remoteFactoryMock.Object, barClientMock.Object, loggerMock.Object);

        // Act
        await sut.EvaluateAsync();

        // Assert
        sut.Result.Should().Be(HealthResult.Failed);
        sut.DependenciesMissingSubscriptions.Should().Contain(depMissing);
        sut.ConflictingSubscriptions.Any().Should().BeFalse();
    }

    // Helper to create a Build that contains given assets
    private static Build NewBuildWithAssets(int id, List<Asset> assets)
    {
        return new Build(
            id: id,
            dateProduced: DateTimeOffset.UtcNow,
            staleness: 0,
            released: false,
            stable: false,
            commit: "sha",
            channels: new List<Channel>(),
            assets: assets,
            dependencies: new List<BuildRef>(),
            incoherencies: new List<BuildIncoherence>());
    }
}

public class SubscriptionConflictTests
{
    /// <summary>
    /// Validates that the constructor assigns input parameters directly to readonly fields.
    /// Inputs (parameterized):
    ///  - asset: null, empty, whitespace, long string, special characters.
    ///  - subscriptions: null, empty list, single item, multiple items with duplicates.
    ///  - utilized: both true and false.
    /// Expected:
    ///  - Asset equals the provided asset reference/value.
    ///  - Subscriptions is the exact same list instance as provided (reference equality), or null.
    ///  - Utilized equals the provided boolean.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(GetConstructorCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AssignsFields_AsProvided(string asset, List<Subscription> subscriptions, bool utilized)
    {
        // Arrange
        // (Inputs provided by TestCaseSource)

        // Act
        var conflict = new SubscriptionConflict(asset, subscriptions, utilized);

        // Assert
        conflict.Asset.Should().Be(asset);
        conflict.Subscriptions.Should().BeSameAs(subscriptions);
        conflict.Utilized.Should().Be(utilized);
    }

    private static IEnumerable<TestCaseData> GetConstructorCases()
    {
        var subA = new Subscription(
            id: Guid.NewGuid(),
            enabled: true,
            sourceEnabled: true,
            sourceRepository: "https://github.com/org/srcA",
            targetRepository: "https://github.com/org/target",
            targetBranch: "main",
            sourceDirectory: "/src",
            targetDirectory: "/tgt",
            pullRequestFailureNotificationTags: "",
            excludedAssets: new List<string>());

        var subB = new Subscription(
            id: Guid.NewGuid(),
            enabled: false,
            sourceEnabled: true,
            sourceRepository: "https://github.com/org/srcB",
            targetRepository: "https://github.com/org/target",
            targetBranch: "release",
            sourceDirectory: "dir",
            targetDirectory: "dir2",
            pullRequestFailureNotificationTags: "tag1,tag2",
            excludedAssets: new List<string> { "X" });

        yield return new TestCaseData(null, null, false)
            .SetName("Constructor_NullAssetAndNullSubscriptions_AssignsFields");

        yield return new TestCaseData(string.Empty, new List<Subscription>(), true)
            .SetName("Constructor_EmptyAssetAndEmptySubscriptions_AssignsFields");

        yield return new TestCaseData("   ", new List<Subscription> { subA }, false)
            .SetName("Constructor_WhitespaceAssetAndSingleSubscription_AssignsFields");

        yield return new TestCaseData(new string('x', 1024), new List<Subscription> { subA, subB, subA }, true)
            .SetName("Constructor_VeryLongAssetAndDuplicateSubscriptions_AssignsFields");

        yield return new TestCaseData("name/with:special|chars?*", new List<Subscription> { subB }, false)
            .SetName("Constructor_SpecialCharactersAssetAndSingleSubscription_AssignsFields");
    }
}
