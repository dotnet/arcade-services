// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.GitHub.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface ICheckResultService
{
    CheckResult GetCheckResult(NamedBuildReference buildReference, ImmutableList<BuildResultAnalysis> buildResultAnalysis, int pendingBuildNames, bool reportSuccessWithKnownIssues);
}
