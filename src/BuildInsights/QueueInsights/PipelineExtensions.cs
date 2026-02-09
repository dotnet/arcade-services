// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using Microsoft.Internal.Helix.Machines.MatrixOfTruthOutputDeserialization.V1.Models;

namespace QueueInsights;

public static class PipelineExtensions
{
    public static IEnumerable<PipelineOutputModel> PublicProject(this IEnumerable<PipelineOutputModel> outputs)
    {
        return outputs.Where(x => x.Project == "public");
    }

    public static IEnumerable<PipelineOutputModel> GetHelixQueues(this IEnumerable<PipelineOutputModel> outputs,
        string repo)
    {
        return outputs.Where(x => x.IsHelixData() && x.Repository == repo);
    }

    private static bool IsHelixData(this PipelineOutputModel model)
    {
        return model.EnvironmentType != null &&
               model.EnvironmentType.Equals("Helix", StringComparison.InvariantCultureIgnoreCase);
    }

    public static IEnumerable<PipelineOutputModel> BuildMachines(this IEnumerable<PipelineOutputModel> outputs)
    {
        return outputs.Where(x => !x.IsHelixData());
    }

    public static bool IsOnPrem(this PipelineOutputModel model)
    {
        if (!model.IsHelixData())
        {
            throw new ArgumentException("The given model is not a helix pipeline output model", nameof(model));
        }

        return model.EnvironmentDescription != null &&
               model.EnvironmentDescription.Equals("OnPremMachine", StringComparison.OrdinalIgnoreCase);
    }

    public static IEnumerable<PipelineOutputModel> InPipelines(this IEnumerable<PipelineOutputModel> outputs,
        IImmutableSet<int> pipelines)
    {
        return outputs.Where(x => x.PipelineId != null && pipelines.Contains((int)x.PipelineId));
    }

    public static IEnumerable<PipelineOutputModel> InBuildPool(this IEnumerable<PipelineOutputModel> outputs,
        string buildPool)
    {
        return outputs.Where(x => x.EnvironmentType == buildPool);
    }
}
