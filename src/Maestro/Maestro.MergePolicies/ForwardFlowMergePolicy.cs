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

namespace Maestro.MergePolicies;
internal class ForwardFlowMergePolicy : CodeFlowMergePolicy
{
    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote remote)
    {
        SourceManifest headBranchSourceManifest;
        try
        {
            headBranchSourceManifest = await remote.GetSourceManifestAsync(pr.TargetRepoUrl, pr.HeadBranch);
        }
        catch (Exception)
        {
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

        List<string> configurationErrors = [
            .. CalculateConfigurationErrors(pr, repoNamesToBarIds, repoNamesToCommitSha),
            .. string.IsNullOrEmpty(pr.TargetBranch)
                ? []
                : ValidateChangesOnlyInUpdatedRepoRecord(
                    headBranchSourceManifest,
                    await remote.GetSourceManifestAsync(pr.TargetRepoUrl, pr.TargetBranch),
                    pr)
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

    private List<string> ValidateChangesOnlyInUpdatedRepoRecord(
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
        var updatedRepo = pr.ContainedUpdates.First().SourceRepo;

        foreach (var repo in headBranchDic.Keys.Where(r => r != updatedRepo))
        {
            if (headBranchDic[repo].CommitSha != targetBranchDic[repo].CommitSha
                || headBranchDic[repo].BarId != targetBranchDic[repo].BarId)
            {
                validationErrors.Add($"Repo {repo} metadata has changed. Only changes to the updated repository ({updatedRepo}) are allowed in a forward flow PR.");
            }
        }

        return validationErrors;
    }

    private static List<string> CalculateConfigurationErrors(
        PullRequestUpdateSummary pr,
        Dictionary<string, int?> repoNamesToBarIds,
        Dictionary<string, string> repoNamesToCommitSha)
    {
        List<string> configurationErrors = [];
        foreach (SubscriptionUpdateSummary PRUpdateSummary in pr.ContainedUpdates)
        {
            if (!repoNamesToBarIds.TryGetValue(PRUpdateSummary.SourceRepo, out int? sourceManifestBarId) || sourceManifestBarId == null)
            {
                configurationErrors.Add($"""
                    #### {configurationErrors.Count + 1}. Missing BAR ID in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                    - **Source Repository**: {PRUpdateSummary.SourceRepo}
                    - **Error**: The BAR ID for the current update from the source repository is not found in the source manifest.
                    """);
            }
            if (sourceManifestBarId != null && sourceManifestBarId != PRUpdateSummary.BuildId)
            {
                configurationErrors.Add($"""
                     #### {configurationErrors.Count + 1}. BAR ID Mismatch in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                     - **Source Repository**: {PRUpdateSummary.SourceRepo}
                     - **Error**: BAR ID `{sourceManifestBarId}` found in the source manifest does not match the build ID of the current update (`{PRUpdateSummary.BuildId}`).
                     """);
            }
            if (!repoNamesToCommitSha.TryGetValue(PRUpdateSummary.SourceRepo, out string sourceManifestCommitSha) || string.IsNullOrEmpty(sourceManifestCommitSha))
            {
                configurationErrors.Add($"""
                    #### {configurationErrors.Count + 1}. Missing Commit SHA in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                    - **Source Repository**: {PRUpdateSummary.SourceRepo}
                    - **Error**: The commit SHA for the current update from the source repository is not found in the source manifest.
                    """);
            }
            if (!string.IsNullOrEmpty(sourceManifestCommitSha) && sourceManifestCommitSha != PRUpdateSummary.CommitSha)
            {
                configurationErrors.Add($"""
                     #### {configurationErrors.Count + 1}. Commit SHA Mismatch in `{VmrInfo.DefaultRelativeSourceManifestPath}`
                     - **Source Repository**: {PRUpdateSummary.SourceRepo}
                     - **Error**: Commit SHA `{sourceManifestCommitSha}` found in the source manifest does not match the commit SHA of the current update (`{PRUpdateSummary.CommitSha}`).
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

