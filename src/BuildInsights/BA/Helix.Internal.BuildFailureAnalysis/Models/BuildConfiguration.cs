using System.Collections.Generic;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class BuildConfiguration
    {
        public bool RetryByAnyError { get; set; }

        public int RetryCountLimit { get; set; }

        public List<Errors> RetryByErrors { get; set; } = new List<Errors>();

        public Pipeline RetryByPipeline { get; set; }

        public RetryByErrorsInPipeline RetryByErrorsInPipeline { get; set; }
    }

    public class Errors
    {
        public string ErrorRegex { get; set; }
    }

    public class Pipeline
    {
        public List<Job> RetryJobs { get; set; } = new List<Job>();

        public List<Stage> RetryStages { get; set; } = new List<Stage>();

        public List<Phase> RetryPhases { get; set; } = new List<Phase>();

        public List<JobsInStage> RetryJobsInStage { get; set; } = new List<JobsInStage>();
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

        public List<string> JobsNames { get; set; } = new List<string>();
    }

    public class RetryByErrorsInPipeline
    {
        public List<ErrorInPipelineByStage> ErrorInPipelineByStage { get; set; } = new List<ErrorInPipelineByStage>();

        public List<ErrorInPipelineByJobs> ErrorInPipelineByJobs { get; set; } = new List<ErrorInPipelineByJobs>();

        public List<ErrorInPipelineByJobsInStage> ErrorInPipelineByJobsInStage { get; set; } = new List<ErrorInPipelineByJobsInStage>();
    }

    public class ErrorInPipelineByStage
    {
        public string StageName { get; set; }

        public string ErrorRegex { get; set; }
    }

    public class ErrorInPipelineByJobs
    {
        public List<string> JobsNames { get; set; } = new List<string>();

        public string ErrorRegex { get; set; }
    }

    public class ErrorInPipelineByJobsInStage
    {
        public string StageName { get; set; }

        public List<string> JobsNames { get; set; } = new List<string>();

        public string ErrorRegex { get; set; }
    }
}
