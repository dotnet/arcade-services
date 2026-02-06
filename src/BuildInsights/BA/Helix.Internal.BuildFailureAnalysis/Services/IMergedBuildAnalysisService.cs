// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services
{
    public interface IMergedBuildAnalysisService
    {
        Task<MergedBuildResultAnalysis> GetMergedAnalysisAsync(
            NamedBuildReference referenceBuild,
            MergeBuildAnalysisAction action,
            CancellationToken cancellationToken);
    }

    public enum MergeBuildAnalysisAction
    {
        Include,
        Exclude,
    }
}
