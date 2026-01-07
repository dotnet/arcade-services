// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
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
        SourceManifest sourceManifest;
        try
        {
            sourceManifest = await remote.GetSourceManifestAsync(pr.TargetRepoUrl, pr.HeadBranch);
        }
        catch (Exception)
        {
            return FailTransiently(
                "Error while retrieving source manifest",
                $"An issue occurred while retrieving the source manifest. This could be due to a misconfiguration of the `{VmrInfo.DefaultRelativeSourceManifestPath}` file, or because of a server error."
                + SeekHelpMsg);
        }

        if (!TryCreateBarIdDictionaryFromSourceManifest(sourceManifest, out Dictionary<string, int?> repoNamesToBarIds) ||
            !TryCreateCommitShaDictionaryFromSourceManifest(sourceManifest, out Dictionary<string, string> repoNamesToCommitSha))
        {
            return FailDecisively(
                "The source manifest file is malformed",
                $"Duplicate repository URIs were found in {VmrInfo.DefaultRelativeSourceManifestPath}." + SeekHelpMsg);
        }

        List<string> configurationErrors = CalculateConfigurationErrors(pr, repoNamesToBarIds, repoNamesToCommitSha);

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

