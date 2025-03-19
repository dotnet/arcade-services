// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

namespace Maestro.MergePolicies;
internal class ForwardFlowMergePolicy : MergePolicy
{
    public override string DisplayName => "ForwardFlow";

    private static readonly string configurationErrorsHeader = """
         ### :x: Check Failed

         The following error(s) were encountered:


        """;

    private static readonly string seekHelpMessage = """

        ### :exclamation: IMPORTANT

        The source-manifest.json and Version.Details.xml files are managed by the Maestro bot. Outside of exceptional circumstances, these files should not be modified manually.
        **Unless you are sure that you know what you are doing, we recommend reaching out for help**. You can receive assistance by:
        - tagging the @dotnet/product-construction team in a PR comment
        - using the [First Responder channel](https://teams.microsoft.com/l/channel/19%3Aafba3d1545dd45d7b79f34c1821f6055%40thread.skype/First%20Responders?groupId=4d73664c-9f2f-450d-82a5-c2f02756606dhttps://teams.microsoft.com/l/channel/19%3Aafba3d1545dd45d7b79f34c1821f6055%40thread.skype/First%20Responders?groupId=4d73664c-9f2f-450d-82a5-c2f02756606d),
        - [opening an issue](https://github.com/dotnet/arcade-services/issues/new?template=BLANK_ISSUE) in the dotnet/arcade-services repo
        - contacting the [.NET Product Construction Services team via e-mail](mailto:dotnetprodconsvcs@microsoft.com).
        """;


    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote darc)
    {
        SourceManifest sourceManifest;
        try
        {
            sourceManifest = await darc.getSourceManifestFromBranch(pr.TargetRepoUrl, pr.HeadBranch);
        }
        catch (Exception)
        {
            return Fail(
                "Error while retrieving source manifest",
                "An issue occurred while retrieving the source manifest. This could be due to a misconfiguration of the source-manifest.json file, or because of a server error.\n"
                + seekHelpMessage
                );
        }

        Dictionary<string, int?> repoNamesToBarIds;
        Dictionary<string, string> repoNamesToCommitSha;
        if (!CreateBarIdDictionaryFromSourceManifest(sourceManifest, out repoNamesToBarIds)||
            !CreateCommitShaDictionaryFromSourceManifest(sourceManifest, out repoNamesToCommitSha))
        {
            return Fail(
                "The source manifest file is malformed",
                "Duplicate repository URIs were found in source-manifest.json.\n" + seekHelpMessage
                );
        }

        List<string> configurationErrors = CalculateConfigurationErrors(pr, repoNamesToBarIds, repoNamesToCommitSha);

        if (configurationErrors.Any())
        {
            string failureMessage = string.Concat(
                configurationErrorsHeader,
                string.Join(Environment.NewLine, configurationErrors) + "\n",
                seekHelpMessage);
            return Fail("Missing or mismatched values found in source-manifest.json", failureMessage);
        }
        else
        {
            return Succeed($"Forward-flow checks succeeded.");
        }
    }

    private List<string> CalculateConfigurationErrors(
        PullRequestUpdateSummary pr,
        Dictionary<string, int?> repoNamesToBarIds,
        Dictionary<string, string> repoNamesToCommitSha)
    {
        List<string> configurationErrors = new();
        int i = 1;
        foreach (SubscriptionUpdateSummary PRUpdateSummary in pr.ContainedUpdates)
        {
            if (!repoNamesToBarIds.TryGetValue(PRUpdateSummary.SourceRepo, out int? sourceManifestBarId) || sourceManifestBarId == null)
            {
                configurationErrors.Add($"""
                    #### {i}. Missing BAR ID in Source Manifest
                    - **Source Repository**: {PRUpdateSummary.SourceRepo}
                    - **Error**: The BAR ID for the current update from the source repository is not found in the source manifest.
                    """);
                i++;
            }
            if (sourceManifestBarId != null && sourceManifestBarId != PRUpdateSummary.BuildId)
            {
                configurationErrors.Add(
                    $"""
                     #### {i}. BAR ID Mismatch in Source Manifest
                     - **Source Repository**: {PRUpdateSummary.SourceRepo}
                     - **Error**: BAR ID `{sourceManifestBarId}` found in the manifest does not match the build ID of the current update (`{PRUpdateSummary.BuildId}`).
                     """);
                i++;
            }
            if (!repoNamesToCommitSha.TryGetValue(PRUpdateSummary.SourceRepo, out string sourceManifestCommitSha) || string.IsNullOrEmpty(sourceManifestCommitSha))
            {
                configurationErrors.Add($"""
                    #### {i}. Missing Commit SHA in Source Manifest
                    - **Source Repository**: {PRUpdateSummary.SourceRepo}
                    - **Error**: The commit SHA for the current update from the source repository is not found in the source manifest.
                    """);
                i++;
            }
            if (!string.IsNullOrEmpty(sourceManifestCommitSha) && sourceManifestCommitSha != PRUpdateSummary.SourceSHA)
            {
                configurationErrors.Add(
                    $"""
                     #### {i}. Commit SHA Mismatch in Source Manifest
                     - **Source Repository**: {PRUpdateSummary.SourceRepo}
                     - **Error**: Commit SHA `{sourceManifestCommitSha}` found in the manifest does not match the commit SHA of the current update (`{PRUpdateSummary.SourceSHA}`).
                     """);
                i++;
            }
        }
        return configurationErrors;
    }

    private bool CreateBarIdDictionaryFromSourceManifest(SourceManifest sourceManifest, out Dictionary<string, int?> repoNamesToBarIds)
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


    private bool CreateCommitShaDictionaryFromSourceManifest(SourceManifest sourceManifest, out Dictionary<string, string> repoNamesToCommitSha)
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

