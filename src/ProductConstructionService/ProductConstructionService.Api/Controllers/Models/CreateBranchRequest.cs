// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Controllers.Models;

#nullable disable
public class CreateBranchRequest
{
    public string SubscriptionId { get; set; }
    public int BuildId { get; set; }
    public string TargetBranch { get; set; }
}
