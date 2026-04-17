// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace BuildInsights.BuildAnalysis.WorkItems.Models;

public class TestBuildAnalysisRequestWorkItem : BuildAnalysisRequestWorkItem
{
    public string MockPrUrl { get; set; }
}
