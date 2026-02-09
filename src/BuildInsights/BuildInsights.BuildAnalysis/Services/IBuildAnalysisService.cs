// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;
using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface IBuildAnalysisService
{
    public Task<BuildResultAnalysis> GetBuildResultAnalysisAsync(BuildReferenceIdentifier buildReference, CancellationToken cancellationToken, bool isValidationAnalysis = false);
}
