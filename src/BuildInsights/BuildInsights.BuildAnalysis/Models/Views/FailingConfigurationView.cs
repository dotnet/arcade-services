// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models.Views;

public class FailingConfigurationView
{
    public string Configuration { get; set; } // ex. Windows.10.Amd64.Open
    public string TestLogs { get; set; }
    public string HistoryLink { get; set; }
    public string ArtifactLink { get; set; } = "https://example.test";
}
