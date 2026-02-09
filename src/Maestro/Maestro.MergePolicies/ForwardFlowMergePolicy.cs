// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Maestro.MergePolicies;
internal class ForwardFlowMergePolicy(IBasicBarClient barClient, ILogger<IMergePolicy> logger) : CodeFlowMergePolicy(barClient, logger)
{
    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote remote)
    {
        // codeflow subscriptions can't be batched, so there can only be one update (the updates are per subscription)
        if (pr.ContainedUpdates.Count == 0)
        {
            _logger.LogError("No updates found in PR {PrUrl} when calculating {policy}", pr.Url, Name);
            return FailDecisively(
                "No updates found in pull request",
                $"The pull request does not contain any updates to evaluate. This could be due to a misconfiguration of the subscription or an issue with the pull request itself."
                + SeekHelpMsg);
        }
        if (pr.ContainedUpdates.Count > 1)
        {
            _logger.LogError("Updates from multile subscriptions found in forward flow pr {PrUrl} when calculating {policy}", pr.Url, Name);
            return FailDecisively(
                "Multiple subscription IDs found in pull request updates",
                $"The pull request contains updates from multiple subscriptions, which is an illegal state."
                + SeekHelpMsg);
        }
        var update = pr.ContainedUpdates.First();

        var subscription = await _barClient.GetSubscriptionAsync(update.SubscriptionId);
        if (subscription == null)
        {
            _logger.LogError("Subscription with ID {SubscriptionId} not found when calculating {policy} PR {PrUrl}", pr.ContainedUpdates.First().SubscriptionId, Name, pr.Url);
            return FailTransiently(
                "Error while retrieving subscription information",
                $"An issue occurred while retrieving subscription information for the pull request updates. This could be due to a transient server error."
                + SeekHelpMsg);
        }
        if (string.IsNullOrEmpty(subscription.TargetDirectory))
        {
            _logger.LogError("Forwardflow Subscription {SubscriptionId} has null or empty TargetDirectory when calculating {policy} PR {PrUrl}", subscription.Id, Name, pr.Url);
            return FailDecisively(
                "Subscription misconfiguration: Target directory not set",
                $"The subscription associated with this pull request does not have a target directory configured. Please ensure that the subscription is correctly configured with a target directory."
                + SeekHelpMsg);
        }
        var mapping = subscription.TargetDirectory;

        // Get the merge base commit between the head and target branches
        GitDiff diff;
        try
        {
            diff = await remote.GitDiffAsync(pr.TargetRepoUrl, subscription.TargetBranch, pr.HeadBranch);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while calculating merge base for PR {PrUrl}", pr.Url);
            return FailTransiently(
                "Error while calculating merge base",
                $"An issue occurred while calculating the merge base commit. This could be due to a server error."
                + SeekHelpMsg);
        }
        if (string.IsNullOrEmpty(diff.MergeBaseCommit))
        {
            _logger.LogError("Merge base commit not found for PR {PrUrl}", pr.Url);
            return FailTransiently(
                "Could not determine merge base commit",
                $"Unable to determine the merge base commit between the head branch and target branch. This could be due to the branches having no common ancestor."
                + SeekHelpMsg);
        }

        SourceManifest headBranchSourceManifest, mergeBaseSourceManifest;
        try
        {
            headBranchSourceManifest = await remote.GetSourceManifestAsync(pr.TargetRepoUrl, pr.HeadBranch);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while retrieving head branch source manifest for PR {PrUrl}", pr.Url);
            return FailTransiently(
                "Error while retrieving head branch source manifest",
                $"An issue occurred while retrieving the source manifest. This could be due to a misconfiguration of the `{VmrInfo.DefaultRelativeSourceManifestPath}` file, or because of a server error."
                + SeekHelpMsg);
        }
        try
        {
            mergeBaseSourceManifest = await remote.GetSourceManifestAsync(pr.TargetRepoUrl, diff.MergeBaseCommit);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while retrieving merge base source manifest for PR {PrUrl}", pr.Url);
            return FailTransiently(
                "Error while retrieving merge base source manifest",
                $"An issue occurred while retrieving the merge base source manifest. This could be due to a misconfiguration of the `{VmrInfo.DefaultRelativeSourceManifestPath}` file, or because of a server error."
                + SeekHelpMsg);
        }

        List<string> configurationErrors = [
            .. CalculateConfigurationErrors(
                mapping,
                update.BuildId,
                update.CommitSha,
                headBranchSourceManifest),
            .. GetUnexpectedRepoChangeErrors(headBranchSourceManifest, mergeBaseSourceManifest, mapping)
        ];

        if (configurationErrors.Count != 0)
        {
            string failureMessage = string.Concat(
                ConfigurationErrorsHeader,
                string.Join(Environment.NewLine, configurationErrors),
                SeekHelpMsg);
            return FailDecisively($"Unexpected codeflow metadata found in {VmrInfo.DefaultRelativeSourceManifestPath}", failureMessage);
        }

        return SucceedDecisively("Forward flow checks succeeded.");
    }

    private static List<string> GetUnexpectedRepoChangeErrors(
        SourceManifest headBranchSourceManifest,
        SourceManifest targetBranchSourceManifest,
        string mapping)
    {
        var headBranchDic = headBranchSourceManifest.Repositories.ToDictionary(r => r.Path);
        var targetBranchDic = targetBranchSourceManifest.Repositories.ToDictionary(r => r.Path);

        List<string> validationErrors = [];

        foreach (var repo in headBranchDic.Keys.Where(r => r != mapping))
        {
            if (!targetBranchDic.TryGetValue(repo, out var targetRepo))
            {
                validationErrors.Add($"Detected new entry for `{repo}` repository in `{VmrInfo.DefaultRelativeSourceManifestPath}`. Only changes to the `{mapping}` entry are allowed in the current codeflow.");
                continue;
            }

            if (headBranchDic[repo].CommitSha != targetRepo.CommitSha
                || headBranchDic[repo].BarId != targetRepo.BarId)
            {
                validationErrors.Add($"Detected changes to the `{repo}` repository in `{VmrInfo.DefaultRelativeSourceManifestPath}`. Only changes to the `{mapping}` entry are allowed in the current codeflow.");
            }
        }

        foreach (var repo in targetBranchDic.Keys.Where(r => r != mapping && !headBranchDic.ContainsKey(r)))
        {
            validationErrors.Add($"Detected removal of entry `{repo}` repository in `{VmrInfo.DefaultRelativeSourceManifestPath}`. Only changes to the `{mapping}` entry are allowed in the current codeflow.");
        }

        return validationErrors;
    }

    private static List<string> CalculateConfigurationErrors(
        string mapping,
        int expectedBuildId,
        string expectedSha,
        SourceManifest headBranchSourceManifest)
    {
        if (!TryCreateBarIdDictionaryFromSourceManifest(headBranchSourceManifest, out Dictionary<string, int?> repMappingsToBarIds)
                || !TryCreateCommitShaDictionaryFromSourceManifest(headBranchSourceManifest, out Dictionary<string, string> repoMappingsToCommitSha))
        {
            return [$"""
                The source manifest file is malformed,
                Duplicate repository URIs were found in {VmrInfo.DefaultRelativeSourceManifestPath}
                {SeekHelpMsg});
                """];
        }

        List<string> configurationErrors = [];

        if (!repMappingsToBarIds.TryGetValue(mapping, out int? sourceManifestBarId) || sourceManifestBarId == null)
        {
            configurationErrors.Add($"""
                #### {configurationErrors.Count + 1}. Missing BAR ID in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                - **Source Repository**: {mapping}
                - **Error**: The BAR ID for the current update from the source repository is not found in the source manifest.
                """);
        }
        if (sourceManifestBarId != null && sourceManifestBarId != expectedBuildId)
        {
            configurationErrors.Add($"""
                 #### {configurationErrors.Count + 1}. BAR ID Mismatch in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                 - **Source Repository**: {mapping}
                 - **Error**: BAR ID `{sourceManifestBarId}` found in the source manifest does not match the build ID of the current update (`{expectedBuildId}`).
                 """);
        }
        if (!repoMappingsToCommitSha.TryGetValue(mapping, out string sourceManifestCommitSha) || string.IsNullOrEmpty(sourceManifestCommitSha))
        {
            configurationErrors.Add($"""
                #### {configurationErrors.Count + 1}. Missing Commit SHA in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                - **Source Repository**: {mapping}
                - **Error**: The commit SHA for the current update from the source repository is not found in the source manifest.
                """);
        }
        if (!string.IsNullOrEmpty(sourceManifestCommitSha) && sourceManifestCommitSha != expectedSha)
        {
            configurationErrors.Add($"""
                 #### {configurationErrors.Count + 1}. Commit SHA Mismatch in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                 - **Source Repository**: {mapping}
                 - **Error**: Commit SHA `{sourceManifestCommitSha}` found in the source manifest does not match the commit SHA of the current update (`{expectedSha}`).
                 """);
        }

        return configurationErrors;
    }

    private static bool TryCreateBarIdDictionaryFromSourceManifest(SourceManifest sourceManifest, out Dictionary<string, int?> repoNamesToBarIds)
    {
        repoNamesToBarIds = [];
        foreach (var repo in sourceManifest.Repositories)
        {
            if (repoNamesToBarIds.ContainsKey(repo.Path))
            {
                return false;
            }
            repoNamesToBarIds.Add(repo.Path, repo.BarId);
        }
        return true;
    }


    private static bool TryCreateCommitShaDictionaryFromSourceManifest(SourceManifest sourceManifest, out Dictionary<string, string> repoNamesToCommitSha)
    {
        repoNamesToCommitSha = [];
        foreach (var repo in sourceManifest.Repositories)
        {
            if (repoNamesToCommitSha.ContainsKey(repo.Path))
            {
                return false;
            }
            repoNamesToCommitSha.Add(repo.Path, repo.CommitSha);
        }
        return true;
    }
}

