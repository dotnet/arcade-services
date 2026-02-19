// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Migrated from test/BuildInsights/BuildMonitor.Tests/BuildHealthEvaluatorTests.cs
// Updated namespaces from BuildMonitor.Analysis/BuildMonitor.Core to
// BuildInsights.BuildAnalysis/BuildInsights.Core.

using AwesomeAssertions;
using BuildInsights.BuildAnalysis;
using BuildInsights.Core;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests;

[TestFixture]
public class BuildHealthEvaluatorTests
{
    private BuildHealthEvaluator _evaluator = null!;

    [SetUp]
    public void SetUp()
    {
        _evaluator = new BuildHealthEvaluator(minimumSuccessRate: 0.8);
    }

    [Test]
    public void EvaluateHealth_AllSucceeded_ReturnsPerfectSummary()
    {
        var results = new List<BuildStatus>
        {
            BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded,
        };

        var summary = _evaluator.EvaluateHealth("https://github.com/test/repo", "main", results);

        summary.TotalBuilds.Should().Be(3);
        summary.SuccessfulBuilds.Should().Be(3);
        summary.FailedBuilds.Should().Be(0);
        summary.SuccessRate.Should().BeApproximately(1.0, 0.001);
    }

    [Test]
    public void EvaluateHealth_MixedResults_CountsCorrectly()
    {
        var results = new List<BuildStatus>
        {
            BuildStatus.Succeeded,
            BuildStatus.Failed,
            BuildStatus.Succeeded,
            BuildStatus.Cancelled,
            BuildStatus.PartiallySucceeded,
        };

        var summary = _evaluator.EvaluateHealth("https://github.com/test/repo", "main", results);

        summary.TotalBuilds.Should().Be(5);
        summary.SuccessfulBuilds.Should().Be(3); // Succeeded + PartiallySucceeded
        summary.FailedBuilds.Should().Be(2);      // Failed + Cancelled
    }

    [Test]
    public void EvaluateHealth_EmptyList_ReturnsZeroSummary()
    {
        var summary = _evaluator.EvaluateHealth("https://github.com/test/repo", "main", []);

        summary.TotalBuilds.Should().Be(0);
        summary.SuccessfulBuilds.Should().Be(0);
        summary.FailedBuilds.Should().Be(0);
        summary.SuccessRate.Should().Be(0.0);
    }

    [Test]
    public void IsHealthy_AboveThreshold_ReturnsTrue()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 10, 9, 1);
        _evaluator.IsHealthy(summary).Should().BeTrue();
    }

    [Test]
    public void IsHealthy_ExactlyAtThreshold_ReturnsTrue()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 10, 8, 2);
        _evaluator.IsHealthy(summary).Should().BeTrue();
    }

    [Test]
    public void IsHealthy_BelowThreshold_ReturnsFalse()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 10, 7, 3);
        _evaluator.IsHealthy(summary).Should().BeFalse();
    }

    [Test]
    public void IsHealthy_CustomThreshold_UsesProvidedThreshold()
    {
        var strictEvaluator = new BuildHealthEvaluator(minimumSuccessRate: 0.95);
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 10, 9, 1);

        strictEvaluator.IsHealthy(summary).Should().BeFalse();
    }
}
