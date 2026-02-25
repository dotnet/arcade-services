// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable
namespace BuildInsights.BuildAnalysis.Models;

public class BuildConfiguration
{
    public bool RetryByAnyError { get; set; }

    public int RetryCountLimit { get; set; }

    public List<Errors> RetryByErrors { get; set; } = [];

    public Pipeline RetryByPipeline { get; set; }

    public RetryByErrorsInPipeline RetryByErrorsInPipeline { get; set; }
}

public class Errors
{
    public string ErrorRegex { get; set; }
}

public class Pipeline
{
    public List<Job> RetryJobs { get; set; } = [];

    public List<Stage> RetryStages { get; set; } = [];

    public List<Phase> RetryPhases { get; set; } = [];

    public List<JobsInStage> RetryJobsInStage { get; set; } = [];
}

public class Job
{
    public string JobName { get; set; }
}

public class Stage
{
    public string StageName { get; set; }
}

public class Phase
{
    public string PhaseName { get; set; }
}

public class JobsInStage
{
    public string StageName { get; set; }

    public List<string> JobsNames { get; set; } = [];
}

public class RetryByErrorsInPipeline
{
    public List<ErrorInPipelineByStage> ErrorInPipelineByStage { get; set; } = [];

    public List<ErrorInPipelineByJobs> ErrorInPipelineByJobs { get; set; } = [];

    public List<ErrorInPipelineByJobsInStage> ErrorInPipelineByJobsInStage { get; set; } = [];
}

public class ErrorInPipelineByStage
{
    public string StageName { get; set; }

    public string ErrorRegex { get; set; }
}

public class ErrorInPipelineByJobs
{
    public List<string> JobsNames { get; set; } = [];

    public string ErrorRegex { get; set; }
}

public class ErrorInPipelineByJobsInStage
{
    public string StageName { get; set; }

    public List<string> JobsNames { get; set; } = [];

    public string ErrorRegex { get; set; }
}
