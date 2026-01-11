// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common.CodeflowHistory;

namespace ProductConstructionService.Api.Controllers.Models;

/// <summary>
/// API model for codeflow history.
/// </summary>
public class CodeflowHistoryResult
{
    public CodeflowHistoryResult(
        IReadOnlyCollection<CodeflowGraphCommit> forwardFlowHistory,
        IReadOnlyCollection<CodeflowGraphCommit> backflowHistory,
        string repoName,
        string vmrName,
        bool resultIsOutdated)
    {
        ForwardFlowHistory = forwardFlowHistory;
        BackflowHistory = backflowHistory;
        RepoName = repoName;
        VmrName = vmrName;
        ResultIsOutdated = resultIsOutdated;
    }

    public IReadOnlyCollection<CodeflowGraphCommit> ForwardFlowHistory { get; set; } = [];
    public IReadOnlyCollection<CodeflowGraphCommit> BackflowHistory { get; set; } = [];
    public string RepoName { get; set; } = "";
    public string VmrName { get; set; } = "";
    public bool ResultIsOutdated { get; set; }
}
