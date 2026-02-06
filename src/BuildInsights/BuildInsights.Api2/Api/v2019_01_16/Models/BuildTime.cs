// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace ProductConstructionService.Api.v2019_01_16.Models;

public class BuildTime
{
    public BuildTime(int defaultChannelId, double officialTime, double prTime, int goalTime)
    {
        DefaultChannelId = defaultChannelId;
        OfficialBuildTime = officialTime;
        PRBuildTime = prTime;
        GoalTimeInMinutes = goalTime;
    }

    public int DefaultChannelId;
    public double OfficialBuildTime;
    public double PRBuildTime;
    public int GoalTimeInMinutes;
}
