// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;

namespace Maestro.MergePolicies;
internal class BackFlowMergePolicy : CodeFlowMergePolicy
{
    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote remote)
    {
        SourceDependency sourceDependency;
        SubscriptionUpdateSummary update;
        try
        {
            sourceDependency = await remote.GetSourceDependencyAsync(pr.TargetRepoUrl, pr.HeadBranch);
            update = pr.ContainedUpdates.FirstOrDefault() ??
                throw new InvalidOperationException("PR object has no contained updates.");
        }
        catch (Octokit.AuthorizationException)
        {
            return Fail($"Error fetching file `{VersionFiles.VersionDetailsXml}`",
                "A server error occurred while trying to read files from the branch." + SeekHelpMsg);
        }
        catch (System.Xml.XmlException e)
        {
            return Fail($"Error reading file `{VersionFiles.VersionDetailsXml}`",
                $"""
                ### Error: failed to parse file `{VersionFiles.VersionDetailsXml}`.
                The file `{VersionFiles.VersionDetailsXml}` is corrupted or improperly structured.
                **XML Error Details**: `{e.Message}`
                """
                + SeekHelpMsg);
        }
        catch (DarcException e)
        {
            // Here also, DarcException is an xml parsing exception... that's how the version details parser throws it
            // messasges from DarcException types should be safe to expose to the client
            return Fail($"Failed to parse file `{VersionFiles.VersionDetailsXml}`",
                $"""
                ### Error: failed to parse file `{VersionFiles.VersionDetailsXml}`.
                There was some unexpected or missing information in the file.
                **Error Details**: `{e.Message}`
                """
                + SeekHelpMsg);
        }
        catch (Exception)
        {
            return Fail(
                $"Failed to retrieve `{VersionFiles.VersionDetailsXml}` file",
                $"""
                ### Error: unexpected server error.
                An unexpected error occurred in the server while trying to read files from the branch.
                This could be due to a temporary exception and may be resolved automatically within 5-10 minutes.
                If the error persists, please follow the instructions below to ask for support.
                """
                + SeekHelpMsg);
        }

        List<string> configurationErrors = CalculateConfigurationErrors(sourceDependency, pr, update);

        if (configurationErrors.Any())
        {
            string failureMessage = string.Concat(
                configurationErrorsHeader,
                string.Join(Environment.NewLine, configurationErrors),
                SeekHelpMsg);
            return Fail($"Missing or mismatched values found in `{VersionFiles.VersionDetailsXml}`", failureMessage);
        }

        return Succeed($"Backflow checks succeeded.");
    }

    private static List<string> CalculateConfigurationErrors(
        SourceDependency sourceDependency,
        PullRequestUpdateSummary pr,
        SubscriptionUpdateSummary update)
    {
        List<string> configurationErrors = new();
        if (sourceDependency.BarId != update.BuildId)
        {
            configurationErrors.Add($"""
                #### {configurationErrors.Count + 1}. BAR ID Mismatch in `{VersionFiles.VersionDetailsXml}`
                - **Source Repository**: {update.SourceRepo}
                - **Error**: BAR ID `{sourceDependency.BarId}` found in the `{VersionFiles.VersionDetailsXml}` file does not match the BAR ID of the current update (`{update.BuildId}`).
                """);
        }

        if (sourceDependency.Sha != update.CommitSha)
        {
            configurationErrors.Add($"""
                #### {configurationErrors.Count + 1}. Commit SHA Mismatch in `{VersionFiles.VersionDetailsXml}`
                - **Source Repository**: {update.SourceRepo}
                - **Error**: Commit SHA `{sourceDependency.Sha}` found in the `{VersionFiles.VersionDetailsXml}` file does not match the commit SHA of the current update (`{update.CommitSha}`).
                """);
        }

        (string targetRepoName, string _) = GitRepoUrlParser.GetRepoNameAndOwner(pr.TargetRepoUrl);
        if (!targetRepoName.Equals(sourceDependency.Mapping, StringComparison.OrdinalIgnoreCase))
        {
            configurationErrors.Add($"""
                #### {configurationErrors.Count + 1}. Mapping Mismatch in `{VersionFiles.VersionDetailsXml}`
                - **Source Repository**: {update.SourceRepo}
                - **Error**: Mapping value `{sourceDependency.Mapping}` found in the `{VersionFiles.VersionDetailsXml}` file does not match the source repository name of the current update (`{targetRepoName}`).
                """);
        }

        if (sourceDependency.Uri != update.SourceRepo)
        {
            configurationErrors.Add($"""
                #### {configurationErrors.Count + 1}. Source Uri Mismatch in `{VersionFiles.VersionDetailsXml}`
                - **Source Repository**: {update.SourceRepo}
                - **Error**: The Source Uri value `{sourceDependency.Uri}` found in the `{VersionFiles.VersionDetailsXml}` file does not match the source repository of the current update (`{update.SourceRepo}`).
                """);
        }
        return configurationErrors;
    }
}

public class BackFlowMergePolicyBuilder : IMergePolicyBuilder
{
    public string Name => MergePolicyConstants.BackFlowMergePolicyName;

    public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
    {
        return Task.FromResult<IReadOnlyList<IMergePolicy>>([new BackFlowMergePolicy()]);
    }
}

