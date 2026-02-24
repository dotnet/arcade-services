// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using BuildInsights.BuildAnalysis.Models.Views;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Views;

[TestFixture]
public class KnownIssueViewComparerTests
{
    [Test]
    public void CheckRunEqualityComparerTest()
    {
        var knownIssueViewA = new KnownIssueView("A", "B", "C", "D", "E", "F");
        var knownIssueViewB = new KnownIssueView("B", "B", "C", "D", "G", "H");
        var knownIssueViewC = new KnownIssueView("B", "A", "C", "D", "I", "J");

        var knownIssueViewComparer = new KnownIssueViewComparer();
        knownIssueViewComparer.Equals(knownIssueViewA, knownIssueViewB).Should().BeTrue();
        knownIssueViewComparer.Equals(knownIssueViewA, knownIssueViewC).Should().BeFalse();
    }
}
