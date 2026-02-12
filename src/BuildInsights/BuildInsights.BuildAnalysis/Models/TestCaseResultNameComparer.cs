// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

/// <summary>
/// TestCaseResult equality test using only the Automated Test Name
/// </summary>
public class TestCaseResultNameComparer : IEqualityComparer<TestCaseResult>
{
    public bool Equals(TestCaseResult x, TestCaseResult y)
    {
        return x?.Name.Equals(y?.Name) ?? false;
    }

    public int GetHashCode(TestCaseResult obj)
    {
        return obj?.Name.GetHashCode() ?? 0;
    }
}
