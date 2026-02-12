// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace BuildInsights.BuildAnalysis.Models;

/// <summary>
/// Collection of TestCaseR
/// </summary>
public class TestHistoryByBranch
{
    public GitRef RefName { get; }
    public ImmutableList<TestCaseResult> Results { get; }

    public TestHistoryByBranch(GitRef refName, ImmutableList<TestCaseResult> results)
    {
        RefName = refName;
        Results = results;
    }

    public TestHistoryByBranch(GitRef refName, IEnumerable<TestCaseResult> results)
        : this(refName, results.ToImmutableList())
    {
    }
}
