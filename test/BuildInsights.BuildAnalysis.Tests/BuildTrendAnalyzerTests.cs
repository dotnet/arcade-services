// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// BuildTrendAnalyzer is a new class introduced during the migration from BuildMonitor
// to BuildInsights.BuildAnalysis. These tests cover the new functionality.

using AwesomeAssertions;
using BuildInsights.BuildAnalysis;
using BuildInsights.Core;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests;

[TestFixture]
public class BuildTrendAnalyzerTests
{
    private BuildTrendAnalyzer _analyzer = null!;

    [SetUp]
    public void SetUp()
    {
        _analyzer = new BuildTrendAnalyzer();
    }

    [Test]
    public void AnalyzeTrend_EmptyRecentBuilds_ReturnsStable()
    {
        var historic = new List<BuildStatus> { BuildStatus.Succeeded, BuildStatus.Failed };

        var trend = _analyzer.AnalyzeTrend([], historic);

        trend.Should().Be(BuildTrend.Stable);
    }

    [Test]
    public void AnalyzeTrend_EmptyHistoricBuilds_ReturnsStable()
    {
        var recent = new List<BuildStatus> { BuildStatus.Succeeded, BuildStatus.Failed };

        var trend = _analyzer.AnalyzeTrend(recent, []);

        trend.Should().Be(BuildTrend.Stable);
    }

    [Test]
    public void AnalyzeTrend_SignificantlyBetterRecentBuilds_ReturnsImproving()
    {
        var historic = new List<BuildStatus> { BuildStatus.Failed, BuildStatus.Failed, BuildStatus.Failed, BuildStatus.Failed, BuildStatus.Succeeded };
        var recent = new List<BuildStatus> { BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded };

        var trend = _analyzer.AnalyzeTrend(recent, historic);

        trend.Should().Be(BuildTrend.Improving);
    }

    [Test]
    public void AnalyzeTrend_SignificantlyWorseRecentBuilds_ReturnsDegrading()
    {
        var historic = new List<BuildStatus> { BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded };
        var recent = new List<BuildStatus> { BuildStatus.Failed, BuildStatus.Failed, BuildStatus.Failed, BuildStatus.Failed, BuildStatus.Succeeded };

        var trend = _analyzer.AnalyzeTrend(recent, historic);

        trend.Should().Be(BuildTrend.Degrading);
    }

    [Test]
    public void AnalyzeTrend_SimilarSuccessRate_ReturnsStable()
    {
        var historic = new List<BuildStatus> { BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Failed };
        var recent = new List<BuildStatus> { BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Succeeded, BuildStatus.Failed };

        var trend = _analyzer.AnalyzeTrend(recent, historic);

        trend.Should().Be(BuildTrend.Stable);
    }
}
