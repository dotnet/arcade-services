// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;

namespace BuildInsights.BuildAnalysis.Models.Views;

public class AttemptView
{
    public string CheckSig { get; set; }
    public string LinkToBuild { get; set; }
    public int AttemptId { get; set; }
    public List<StepResultView> BuildStepsResult { get; set; } = new List<StepResultView>();
    public List<TestResultView> TestResults { get; set; } = new List<TestResultView>();
    public bool HasTestFailures => TestResults != null && TestResults.Count > 0;
    public bool HasBuildFailures => BuildStepsResult != null && BuildStepsResult.Count > 0;
}
