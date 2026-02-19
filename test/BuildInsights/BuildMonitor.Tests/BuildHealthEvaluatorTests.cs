// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NOTE: This file was copied from the old BuildMonitor repository.
// The namespace and using directives reflect the old project structure.
// These tests need to be migrated to BuildInsights.BuildAnalysis.Tests.

using NUnit.Framework;
using AwesomeAssertions;
using BuildMonitor.Analysis; // old namespace - was BuildInsights.BuildAnalysis
using BuildMonitor.Core; // old namespace - was BuildInsights.Core

namespace BuildMonitor.Tests;

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
            BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded
        };

        var summary = _evaluator.EvaluateHealth("https://github.com/test/repo", "main", results);

        summary.TotalBuilds.Should().Be(3);
        summary.SuccessfulBuilds.Should().Be(3);
        summary.FailedBuilds.Should().Be(0);
        summary.SuccessRate.Should().BeApproximately(1.0, 0.001);
    }

    [Test]
    public void IsHealthy_AboveThreshold_ReturnsTrue()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 10, 9, 1);
        _evaluator.IsHealthy(summary).Should().BeTrue();
    }

    [Test]
    public void IsHealthy_BelowThreshold_ReturnsFalse()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 10, 7, 3);
        _evaluator.IsHealthy(summary).Should().BeFalse();
    }
}
