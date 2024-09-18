// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Controllers.Models;

public class AzDoBuild
{
    public required DateTime FinishTime { get; init; }
    public required int Id { get; init; }
}
