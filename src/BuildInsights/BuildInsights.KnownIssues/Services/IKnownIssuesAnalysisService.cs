// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;

namespace BuildInsights.KnownIssues.Services;

public interface IKnownIssuesAnalysisService
{
    Task RequestKnownIssuesAnalysis(string organization, string repository, long issueId);
}
