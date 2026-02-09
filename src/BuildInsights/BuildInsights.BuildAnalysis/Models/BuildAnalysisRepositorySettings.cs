// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.TeamFoundation.Build.WebApi;

namespace BuildInsights.BuildAnalysis.Models;

public class PipelineData
{
    public int PipelineId { get; set; }
    public string PipelineName { get; set; }

    public PipelineData()
    {

    }

    public PipelineData(int pipelineId, string pipelineName)
    {
        PipelineId = pipelineId;
        PipelineName = pipelineName;
    }
}

public class BuildAnalysisRepositorySettings
{
    public bool FilterPipelines => PipelinesToAnalyze is {Count: > 0};
    public List<PipelineData> PipelinesToAnalyze { get; set; }
}
