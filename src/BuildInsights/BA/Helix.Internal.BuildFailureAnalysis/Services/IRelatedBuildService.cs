// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services
{
    public interface IRelatedBuildService
    {
        Task<RelatedBuilds> GetRelatedBuilds(BuildReferenceIdentifier singleBuild, CancellationToken cancellationToken);
        Task<Build> GetRelatedBuildFromCheckRun(string repository, string sourceSha);
    }
}
