// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.KnownIssues.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface ITestResultService
{
    Task<TestKnownIssuesAnalysis> GetTestFailingWithKnownIssuesAnalysis(IReadOnlyList<TestRunDetails> failingTestCaseResults, IReadOnlyList<KnownIssue> knownIssues, string orgId);
}
