// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BuildInsights.KnownIssues.Models;

namespace BuildInsights.KnownIssues.Services;

public interface IKnownIssuesService
{
    Task<ImmutableList<KnownIssueMatch>> GetKnownIssuesMatchesForIssue(int issueId, string issueRepository);
    Task<ImmutableList<TestKnownIssueMatch>> GetTestKnownIssuesMatchesForIssue(int issueId, string issueRepository);
    Task SaveKnownIssuesMatches(int buildId, List<KnownIssueMatch> knownIssueMatches);
    Task SaveTestsKnownIssuesMatches(int buildId, List<TestKnownIssueMatch> knownIssueMatches);
    Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int id);
}
