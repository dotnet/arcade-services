// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using BuildInsights.KnownIssues.Models;
using Octokit;

namespace BuildInsights.KnownIssues.Services;

public interface IGitHubIssuesService
{
    Task<ImmutableList<KnownIssue>> GetCriticalInfrastructureIssuesAsync();
    Task<IEnumerable<KnownIssue>> GetInfrastructureKnownIssues();
    Task<IEnumerable<KnownIssue>> GetRepositoryKnownIssues(string buildRepo);
    Task UpdateIssueBodyAsync(string repository, int issueNumber, string description);
    Task<Issue> GetIssueAsync(string repository, int issueNumber);
    Task AddLabelToIssueAsync(string repository, int issueNumber, string label);
    Task AddCommentToIssueAsync(string repository, int issueNumber, string comment);
}
