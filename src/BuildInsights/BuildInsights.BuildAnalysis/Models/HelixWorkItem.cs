// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class HelixWorkItem
{
    public string HelixJobId { get; set; }
    public string HelixWorkItemName { get; set; }
    public string ConsoleLogUrl { get; set; }
    public int? ExitCode { get; set; }
    public string Status { get; set; }
}
