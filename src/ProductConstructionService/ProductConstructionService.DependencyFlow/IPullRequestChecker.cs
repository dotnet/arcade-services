// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data.Models;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using ProductConstructionService.DependencyFlow.Model;
using ProductConstructionService.DependencyFlow.WorkItems;

namespace ProductConstructionService.DependencyFlow;

/// <summary>
///     Handles PR status checking, merge policy evaluation, and PR lifecycle management.
/// </summary>
public interface IPullRequestChecker
{
    Task<bool> CheckPullRequestAsync(PullRequestCheck pullRequestCheck);

    Task<(PullRequestStatus Status, PullRequest PrInfo)> GetPullRequestStatusAsync(
        InProgressPullRequest pr,
        bool isCodeFlow,
        bool tryingToUpdate);

    Task<(IReadOnlyList<MergePolicyDefinition> policyDefinitions, MergePolicyEvaluationResults updatedResult)> RunMergePolicyEvaluation(
        InProgressPullRequest pr,
        PullRequest prInfo,
        IRemote remote);
}
