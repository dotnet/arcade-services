// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common.CodeflowHistory;

namespace ProductConstructionService.Api.Controllers.Models;

public class CodeflowHistoryResult
{
    public IReadOnlyCollection<CodeflowGraphCommit> ForwardFlowHistory { get; set; } = [];
    public IReadOnlyCollection<CodeflowGraphCommit> BackflowHistory { get; set; } = [];
    public bool ResultIsOutdated { get; set; }
}
