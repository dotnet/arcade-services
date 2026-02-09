// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class PullRequestData
{
    public string Action { get; set; }
    public bool Merged { get; set;  }
    public string Organization { get; set; }
    public string Repository { get; set; }
    public string HeadSha { get; set; }
    public long Number { get; set; }
}
