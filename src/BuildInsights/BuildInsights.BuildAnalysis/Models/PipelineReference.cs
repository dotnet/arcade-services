// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class PipelineReference : IEquatable<PipelineReference>
{
    public PipelineReference(
        StageReference stageReference,
        PhaseReference phaseReference,
        JobReference jobReference)
    {
        StageReference = stageReference;
        PhaseReference = phaseReference;
        JobReference = jobReference;
    }

    public PipelineReference(
        int pipelineId,
        StageReference stageReference,
        PhaseReference phaseReference,
        JobReference jobReference)
    {
        PipelineId = pipelineId;
        StageReference = stageReference;
        PhaseReference = phaseReference;
        JobReference = jobReference;
    }

    public int PipelineId { get; }
    public StageReference StageReference { get; }
    public PhaseReference PhaseReference { get; }
    public JobReference JobReference { get; }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StageReference.StageName);
        hash.Add(PhaseReference.PhaseName);

        return hash.ToHashCode();
    }

    public bool Equals(PipelineReference other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return (string.Equals(StageReference.StageName, other.StageReference.StageName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(PhaseReference.PhaseName, other.PhaseReference.PhaseName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(JobReference.JobName, other.JobReference.JobName, StringComparison.OrdinalIgnoreCase)) ||
               MatchByPipelineConstruction(this, other) ||
               MatchByPipelineConstruction(other, this);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is PipelineReference pipelineReference)
        {
            return Equals(pipelineReference);
        }

        return false;
    }

    public bool MatchByPipelineConstruction(PipelineReference pipeline, PipelineReference pipelineToCompare)
    {
        string stageName = pipeline.StageReference.StageName;
        string phaseName = pipeline.PhaseReference.PhaseName;
        string jobName = pipeline.JobReference.JobName;

        //Scenarios in where the job name contains the information of the stage/phase of the pipeline.
        //JobName = StageName.PhaseName.JobName or JobName = PhaseName.JobName (in the last case we check also the StageName)
        return string.Equals(pipelineToCompare.JobReference.JobName, string.Join(".", stageName, phaseName, jobName), StringComparison.OrdinalIgnoreCase) ||
               (string.Equals(pipeline.StageReference.StageName, pipelineToCompare.StageReference.StageName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pipelineToCompare.JobReference.JobName, string.Join(".", phaseName, jobName), StringComparison.OrdinalIgnoreCase));
    }
}

public class StageReference
{
    public StageReference(string stageName, int attempt)
    {
        StageName = stageName;
        Attempt = attempt;
    }

    public string StageName { get; }
    public int Attempt { get; }
}

public class PhaseReference
{
    public PhaseReference(string phaseName, int attempt)
    {
        PhaseName = phaseName;
        Attempt = attempt;
    }

    public string PhaseName { get; }
    public int Attempt { get; }
}

public class JobReference
{
    public JobReference(string jobName, int attempt)
    {
        JobName = jobName;
        Attempt = attempt;
    }

    public string JobName { get; }
    public int Attempt { get; }
}
