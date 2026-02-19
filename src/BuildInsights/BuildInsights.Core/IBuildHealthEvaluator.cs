// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace BuildInsights.Core;

/// <summary>
/// Evaluates the health of builds for a repository branch.
/// </summary>
public interface IBuildHealthEvaluator
{
    /// <summary>
    /// Evaluates build health based on a list of build results.
    /// </summary>
    BuildHealthSummary EvaluateHealth(string repositoryUrl, string branch, IReadOnlyList<BuildStatus> buildResults);

    /// <summary>
    /// Determines whether the build health is considered acceptable.
    /// </summary>
    bool IsHealthy(BuildHealthSummary summary);
}
