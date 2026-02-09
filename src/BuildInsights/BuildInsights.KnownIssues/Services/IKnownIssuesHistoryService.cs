// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildInsights.KnownIssues.Models;

namespace BuildInsights.KnownIssues.Services;

public interface IKnownIssuesHistoryService
{
    Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int id);
    Task<List<KnownIssueAnalysis>> GetKnownIssuesHistory(string issueRepo, long issueId, DateTimeOffset since, CancellationToken cancellationToken);
    Task SaveKnownIssueError(string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken);
    Task<KnownIssueError> GetLatestKnownIssueError(string issueRepo, long issueId, CancellationToken cancellationToken);
    Task SaveBuildKnownIssueValidation(int buildId, string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken);
    Task<List<KnownIssueAnalysis>> GetBuildKnownIssueValidatedRecords(string buildId, string issueRepo, long issueId, CancellationToken cancellationToken);
}
