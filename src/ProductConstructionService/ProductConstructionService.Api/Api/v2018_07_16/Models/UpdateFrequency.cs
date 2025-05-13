// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.Api.v2018_07_16.Models;

public enum UpdateFrequency
{
    None = 0,
    EveryDay,
    EveryBuild,
    TwiceDaily,
    EveryWeek,
    EveryTwoWeeks,
    EveryMonth,
}
