// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Common;

namespace ProductConstructionService.Api.Controllers.Models;

public class CodeflowHistoryResult
{
    public CodeflowHistory? ForwardFlowHistory { get; set; }
    public CodeflowHistory? BackflowHistory { get; set; }
    public bool ResultIsOutdated { get; set; }
}
