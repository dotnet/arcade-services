// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;

namespace Maestro.MergePolicies;
internal class ForwardFlowMergePolicy : MergePolicy
{
    public override string DisplayName => "ForwardFlow";

    private static readonly string configurationErrorsHeader = """
         ### :x: Check Failed

         The following error(s) were encountered:


        """;

    private static readonly string seekHelpMessage = $"""


        ### :exclamation: IMPORTANT

        The `{VmrInfo.DefaultRelativeSourceManifestPath}` and `{VersionFiles.VersionDetailsXml}` files are managed by Maestro/darc. Outside of exceptional circumstances, these files should not be modified manually.
        **Unless you are sure that you know what you are doing, we recommend reaching out for help**. You can receive assistance by:
        - tagging the **@dotnet/product-construction** team in a PR comment
        - using the [First Responder channel](https://teams.microsoft.com/l/channel/19%3Aafba3d1545dd45d7b79f34c1821f6055%40thread.skype/First%20Responders?groupId=4d73664c-9f2f-450d-82a5-c2f02756606dhttps://teams.microsoft.com/l/channel/19%3Aafba3d1545dd45d7b79f34c1821f6055%40thread.skype/First%20Responders?groupId=4d73664c-9f2f-450d-82a5-c2f02756606d),
        - [opening an issue](https://github.com/dotnet/arcade-services/issues/new?template=BLANK_ISSUE) in the dotnet/arcade-services repo
        - contacting the [.NET Product Construction Services team via e-mail](mailto:dotnetprodconsvcs@microsoft.com).
        """;


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
                + seekHelpMessage);
        }

        Dictionary<string, int?> repoNamesToBarIds;
        Dictionary<string, string> repoNamesToCommitSha;
        if (!TryCreateBarIdDictionaryFromSourceManifest(sourceManifest, out repoNamesToBarIds) ||
            !TryCreateCommitShaDictionaryFromSourceManifest(sourceManifest, out repoNamesToCommitSha))
        {
            return Fail(
                "The source manifest file is malformed",
                $"Duplicate repository URIs were found in {VmrInfo.DefaultRelativeSourceManifestPath}." + seekHelpMessage);
        }

        List<string> configurationErrors = CalculateConfigurationErrors(pr, repoNamesToBarIds, repoNamesToCommitSha);

        if (configurationErrors.Any())
        {
            string failureMessage = string.Concat(
                configurationErrorsHeader,
                string.Join(Environment.NewLine, configurationErrors),
                seekHelpMessage);
            return Fail($"Missing or mismatched values found in {VmrInfo.DefaultRelativeSourceManifestPath}", failureMessage);
        }

        return Succeed($"Forward-flow checks succeeded.");
    }

    private List<string> CalculateConfigurationErrors(
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

    private bool TryCreateBarIdDictionaryFromSourceManifest(SourceManifest sourceManifest, out Dictionary<string, int?> repoNamesToBarIds)
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


    private bool TryCreateCommitShaDictionaryFromSourceManifest(SourceManifest sourceManifest, out Dictionary<string, string> repoNamesToCommitSha)
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

