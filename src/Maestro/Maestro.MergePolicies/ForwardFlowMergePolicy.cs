// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

namespace Maestro.MergePolicies;
internal class ForwardFlowMergePolicy(ILogger<IMergePolicy> logger) : CodeFlowMergePolicy(logger)
{
    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote remote)
    {
        SourceManifest headBranchSourceManifest, targetBranchSourceManifest;
        try
        {
            headBranchSourceManifest = await remote.GetSourceManifestAsync(pr.TargetRepoUrl, pr.HeadBranch);
        }
        catch (Exception)
        {
            _logger.LogError("Error while retrieving head branch source manifest for PR {PrUrl}", pr.Url);
            return FailTransiently(
                "Error while retrieving head branch source manifest",
                $"An issue occurred while retrieving the source manifest. This could be due to a misconfiguration of the `{VmrInfo.DefaultRelativeSourceManifestPath}` file, or because of a server error."
                + SeekHelpMsg);
        }

        if (!TryCreateBarIdDictionaryFromSourceManifest(headBranchSourceManifest, out Dictionary<string, int?> repoNamesToBarIds) ||
            !TryCreateCommitShaDictionaryFromSourceManifest(headBranchSourceManifest, out Dictionary<string, string> repoNamesToCommitSha))
        {
            return FailDecisively(
                "The source manifest file is malformed",
                $"Duplicate repository URIs were found in {VmrInfo.DefaultRelativeSourceManifestPath}." + SeekHelpMsg);
        }

        List<string> configurationErrors = CalculateConfigurationErrors(pr, repoNamesToBarIds, repoNamesToCommitSha);

        List<string> unexpectedRepoChangeErrors = [];
        if (!string.IsNullOrEmpty(pr.TargetBranch))
        {
            try
            {
                targetBranchSourceManifest = await remote.GetSourceManifestAsync(pr.TargetRepoUrl, pr.TargetBranch);
            }
            catch (Exception)
            {
                _logger.LogError("Error while retrieving target branch source manifest for PR {PrUrl}", pr.Url);
                return FailTransiently(
                    "Error while retrieving target branch source manifest",
                    $"An issue occurred while retrieving the target branch source manifest. This could be due to a misconfiguration of the `{VmrInfo.DefaultRelativeSourceManifestPath}` file, or because of a server error."
                    + SeekHelpMsg);
            }

            unexpectedRepoChangeErrors = GetUnexpectedRepoChangeErrors(headBranchSourceManifest, targetBranchSourceManifest, pr);
        }

        configurationErrors.AddRange(unexpectedRepoChangeErrors);

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
        PullRequestUpdateSummary pr)
    {
        var headBranchDic = headBranchSourceManifest.Repositories.ToDictionary(r => r.RemoteUri);
        var targetBranchDic = targetBranchSourceManifest.Repositories.ToDictionary(r => r.RemoteUri);

        List<string> validationErrors = [];

        if (headBranchDic.Keys.Count != targetBranchDic.Keys.Count)
        {
            validationErrors.Add($"The number of repositories in the head branch ({headBranchDic.Keys.Count}) does not match the target branch ({targetBranchDic.Keys.Count}).");
        }

        // codeflow subscriptions can't be batched so there will only be one source repo even for multiple updates
        var update = pr.ContainedUpdates.FirstOrDefault();
        if (update == null)
        {
            return [];
        }

        var updatedRepo = update.SourceRepo;

        foreach (var repo in headBranchDic.Keys.Where(r => r != updatedRepo))
        {
            if (!targetBranchDic.TryGetValue(repo, out var targetRepo))
            {
                validationErrors.Add($"Detected new entry for `{repo}` repository in `{VmrInfo.DefaultRelativeSourceManifestPath}`. Only changes to the `{updatedRepo}` repository are allowed in the current codeflow.");
                continue;
            }

            if (headBranchDic[repo].CommitSha != targetRepo.CommitSha
                || headBranchDic[repo].BarId != targetRepo.BarId)
            {
                validationErrors.Add($"Detected changes to the `{repo}` repository in `{VmrInfo.DefaultRelativeSourceManifestPath}`. Only changes to the `{updatedRepo}` repository are allowed in the current codeflow.");
            }
        }

        foreach (var repo in targetBranchDic.Keys.Where(r => r != updatedRepo && !headBranchDic.ContainsKey(r)))
        {
            validationErrors.Add($"Detected removal of entry `{repo}` repository in `{VmrInfo.DefaultRelativeSourceManifestPath}`. Only changes to the `{updatedRepo}` repository are allowed in the current codeflow.");
        }

        return validationErrors;
    }

    private static List<string> CalculateConfigurationErrors(
        PullRequestUpdateSummary pr,
        Dictionary<string, int?> repoNamesToBarIds,
        Dictionary<string, string> repoNamesToCommitSha)
    {
        List<string> configurationErrors = [];
        foreach (SubscriptionUpdateSummary prUpdateSummary in pr.ContainedUpdates)
        {
            if (!repoNamesToBarIds.TryGetValue(prUpdateSummary.SourceRepo, out int? sourceManifestBarId) || sourceManifestBarId == null)
            {
                configurationErrors.Add($"""
                    #### {configurationErrors.Count + 1}. Missing BAR ID in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                    - **Source Repository**: {prUpdateSummary.SourceRepo}
                    - **Error**: The BAR ID for the current update from the source repository is not found in the source manifest.
                    """);
            }
            if (sourceManifestBarId != null && sourceManifestBarId != prUpdateSummary.BuildId)
            {
                configurationErrors.Add($"""
                     #### {configurationErrors.Count + 1}. BAR ID Mismatch in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                     - **Source Repository**: {prUpdateSummary.SourceRepo}
                     - **Error**: BAR ID `{sourceManifestBarId}` found in the source manifest does not match the build ID of the current update (`{prUpdateSummary.BuildId}`).
                     """);
            }
            if (!repoNamesToCommitSha.TryGetValue(prUpdateSummary.SourceRepo, out string sourceManifestCommitSha) || string.IsNullOrEmpty(sourceManifestCommitSha))
            {
                configurationErrors.Add($"""
                    #### {configurationErrors.Count + 1}. Missing Commit SHA in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                    - **Source Repository**: {prUpdateSummary.SourceRepo}
                    - **Error**: The commit SHA for the current update from the source repository is not found in the source manifest.
                    """);
            }
            if (!string.IsNullOrEmpty(sourceManifestCommitSha) && sourceManifestCommitSha != prUpdateSummary.CommitSha)
            {
                configurationErrors.Add($"""
                     #### {configurationErrors.Count + 1}. Commit SHA Mismatch in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                     - **Source Repository**: {prUpdateSummary.SourceRepo}
                     - **Error**: Commit SHA `{sourceManifestCommitSha}` found in the source manifest does not match the commit SHA of the current update (`{prUpdateSummary.CommitSha}`).
                     """);
            }
        }
        return configurationErrors;
    }

    private static bool TryCreateBarIdDictionaryFromSourceManifest(SourceManifest sourceManifest, out Dictionary<string, int?> repoNamesToBarIds)
    {
        repoNamesToBarIds = [];
        foreach (var repo in sourceManifest.Repositories)
        {
            if (repoNamesToBarIds.ContainsKey(repo.RemoteUri))
            {
                return false;
            }
            repoNamesToBarIds.Add(repo.RemoteUri, repo.BarId);
        }
        return true;
    }


    private static bool TryCreateCommitShaDictionaryFromSourceManifest(SourceManifest sourceManifest, out Dictionary<string, string> repoNamesToCommitSha)
    {
        repoNamesToCommitSha = [];
        foreach (var repo in sourceManifest.Repositories)
        {
            if (repoNamesToCommitSha.ContainsKey(repo.RemoteUri))
            {
                return false;
            }
            repoNamesToCommitSha.Add(repo.RemoteUri, repo.CommitSha);
        }
        return true;
    }
}

