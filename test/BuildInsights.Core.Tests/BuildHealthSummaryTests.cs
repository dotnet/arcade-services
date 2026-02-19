// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Migrated from test/BuildInsights/BuildMonitor.Tests/BuildHealthSummaryTests.cs
// Updated namespace from BuildMonitor.Core to BuildInsights.Core.

using AwesomeAssertions;
using BuildInsights.Core;
using NUnit.Framework;

namespace BuildInsights.Core.Tests;

[TestFixture]
public class BuildHealthSummaryTests
{
    [Test]
    public void Constructor_SetsAllProperties()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 10, 8, 2);

        summary.RepositoryUrl.Should().Be("https://github.com/test/repo");
        summary.Branch.Should().Be("main");
        summary.TotalBuilds.Should().Be(10);
        summary.SuccessfulBuilds.Should().Be(8);
        summary.FailedBuilds.Should().Be(2);
    }

    [Test]
    public void SuccessRate_WithBuilds_ReturnsCorrectRate()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 10, 8, 2);
        summary.SuccessRate.Should().BeApproximately(0.8, 0.001);
    }

    [Test]
    public void SuccessRate_WithNoBuilds_ReturnsZero()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 0, 0, 0);
        summary.SuccessRate.Should().Be(0.0);
    }

    [Test]
    public void SuccessRate_AllFailed_ReturnsZero()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 5, 0, 5);
        summary.SuccessRate.Should().Be(0.0);
    }

    [Test]
    public void SuccessRate_AllSucceeded_ReturnsOne()
    {
        var summary = new BuildHealthSummary("https://github.com/test/repo", "main", 5, 5, 0);
        summary.SuccessRate.Should().BeApproximately(1.0, 0.001);
    }
}
