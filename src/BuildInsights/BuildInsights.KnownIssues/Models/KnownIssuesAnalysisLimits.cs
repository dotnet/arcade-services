// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.KnownIssues.Models;

public class KnownIssuesAnalysisLimits
{
    public int RecordCountLimit { get; set; }
    public int LogLinesCountLimit { get; set; }
    public int FailingTestCountLimit { get; set; }
    public int HelixLogsFilesLimit { get; set; }
}
