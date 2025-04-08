﻿// Licensed to the .NET Foundation under one or more agreements.
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
        SourceManifest sourceManifest;
        try
        {
            sourceManifest = await remote.GetSourceManifestAsync(pr.TargetRepoUrl, pr.HeadBranch);
        }
        catch (Exception)
        {
            return Fail(
                "Error while retrieving source manifest",
                $"An issue occurred while retrieving the source manifest. This could be due to a misconfiguration of the `{VmrInfo.DefaultRelativeSourceManifestPath}` file, or because of a server error."
                + SeekHelpMsg);
        }

        Dictionary<string, int?> repoNamesToBarIds;
        Dictionary<string, string> repoNamesToCommitSha;
        if (!TryCreateBarIdDictionaryFromSourceManifest(sourceManifest, out repoNamesToBarIds) ||
            !TryCreateCommitShaDictionaryFromSourceManifest(sourceManifest, out repoNamesToCommitSha))
        {
            return Fail(
                "The source manifest file is malformed",
                $"Duplicate repository URIs were found in {VmrInfo.DefaultRelativeSourceManifestPath}." + SeekHelpMsg);
        }

        List<string> configurationErrors = CalculateConfigurationErrors(pr, repoNamesToBarIds, repoNamesToCommitSha);

        if (configurationErrors.Any())
        {
            string failureMessage = string.Concat(
                configurationErrorsHeader,
                string.Join(Environment.NewLine, configurationErrors),
                SeekHelpMsg);
            return Fail($"Missing or mismatched values found in {VmrInfo.DefaultRelativeSourceManifestPath}", failureMessage);
        }

        return Succeed($"Forward-flow checks succeeded.");
    }

    private static List<string> CalculateConfigurationErrors(
        PullRequestUpdateSummary pr,
        Dictionary<string, int?> repoNamesToBarIds,
        Dictionary<string, string> repoNamesToCommitSha)
    {
        List<string> configurationErrors = new();
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
        repoNamesToBarIds = new Dictionary<string, int?>();
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
        repoNamesToCommitSha = new Dictionary<string, string>();
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

public class ForwardFlowMergePolicyBuilder : IMergePolicyBuilder
{
    public string Name => MergePolicyConstants.ForwardFlowMergePolicyName;

    public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
    {
        return Task.FromResult<IReadOnlyList<IMergePolicy>>([new ForwardFlowMergePolicy()]);
    }
}

