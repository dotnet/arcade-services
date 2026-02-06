// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Services;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Providers
{
    public class PreviousBuildAnalysisProvider : IPreviousBuildAnalysisService
    {
        private readonly IBuildCacheService _cache;

        public PreviousBuildAnalysisProvider(IBuildCacheService cache)
        {
            _cache = cache;
        }

        public Task<BuildResultAnalysis> GetBuildResultAnalysisAsync(BuildReferenceIdentifier buildReference, CancellationToken cancellationToken, bool isValidationAnalysis = false)
        {
            return _cache.TryGetBuildAsync(buildReference, cancellationToken);
        }
    }
}
