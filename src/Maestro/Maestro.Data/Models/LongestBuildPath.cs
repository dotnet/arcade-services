// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Maestro.Data.Models;

public class LongestBuildPath
{
    public int Id { get; set; }
    public int ChannelId { get; set; }
    public Channel Channel { get; set; }
    public DateTimeOffset ReportDate { get; set; }
    public double BestCaseTimeInMinutes { get; set; }
    public double WorstCaseTimeInMinutes { get; set; }
    public string ContributingRepositories { get; set; }
}
