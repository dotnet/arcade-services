// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Internal.Helix.Machines.MatrixOfTruthOutputDeserialization.V1.Models;

namespace QueueInsights.Services;

public interface IMatrixOfTruthService
{
    public Task<IList<PipelineOutputModel>> GetPipelineOutputsAsync();
}
