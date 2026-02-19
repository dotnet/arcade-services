// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NOTE: This file was copied from the old BuildMonitor repository.
// The namespace and using directives reflect the old project structure.
// These tests need to be migrated to BuildInsights.Core.Tests.

using NUnit.Framework;
using AwesomeAssertions;
using BuildMonitor.Core; // old namespace - was BuildInsights.Core

namespace BuildMonitor.Tests;

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
}
