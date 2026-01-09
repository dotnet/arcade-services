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
        catch (System.Xml.XmlException e)
        {
            return FailDecisively($"Error reading file `{VersionFiles.VersionDetailsXml}`",
                $"""
                ### :warning: Error: failed to parse file `{VersionFiles.VersionDetailsXml}`.
                The file `{VersionFiles.VersionDetailsXml}` is corrupted or improperly structured.
                > XML Error: {e.Message}
                """
                + SeekHelpMsg);
        }
        catch (DarcException e)
        {
            // Here also, DarcException is an xml parsing exception... that's how the version details parser throws it
            // messasges from DarcException types should be safe to expose to the client
            return FailDecisively($"Failed to parse file `{VersionFiles.VersionDetailsXml}`",
                $"""
                ### :warning: Error: failed to parse file `{VersionFiles.VersionDetailsXml}`.
                There was some unexpected or missing information in the file.
                > Error: {e.Message}
                """
                + SeekHelpMsg);
        }
        catch (Exception)
        {
            return FailTransiently(
                $"Failed to retrieve file `{VersionFiles.VersionDetailsXml}`",
                $"""
                ### :warning: Error: unexpected server error.
                An unexpected error occurred in the server while trying to read files from the branch.
                This could be due to a temporary exception and may be resolved automatically within 5-10 minutes.
                If the error persists, please follow the instructions below to ask for support.
                """
                + SeekHelpMsg);
        }

        List<string> configurationErrors = CalculateConfigurationErrors(sourceDependency, update);

        if (configurationErrors.Count != 0)
        {
            string failureMessage = string.Concat(
                ConfigurationErrorsHeader,
                string.Join(Environment.NewLine, configurationErrors),
                SeekHelpMsg);
            return FailDecisively($"Unexpected codeflow metadata found in `{VersionFiles.VersionDetailsXml}`", failureMessage);
        }

        return SucceedDecisively("Backflow checks succeeded.");
    }

    private static List<string> CalculateConfigurationErrors(
        SourceDependency sourceDependency,
        SubscriptionUpdateSummary update)
    {
        List<string> configurationErrors = [];
        if (sourceDependency.BarId != update.BuildId)
        {
            configurationErrors.Add($"""
                #### {configurationErrors.Count + 1}. BAR ID Mismatch in `{VersionFiles.VersionDetailsXml}`
                - **Source Repository**: {update.SourceRepo}
                - **Error**: BAR ID `{sourceDependency.BarId}` found in `{VersionFiles.VersionDetailsXml}` does not match the BAR ID of the current update (`{update.BuildId}`).
                """);
        }

        if (sourceDependency.Sha != update.CommitSha)
        {
            configurationErrors.Add($"""
                #### {configurationErrors.Count + 1}. Commit SHA Mismatch in `{VersionFiles.VersionDetailsXml}`
                - **Source Repository**: {update.SourceRepo}
                - **Error**: Commit SHA `{sourceDependency.Sha}` found in `{VersionFiles.VersionDetailsXml}` does not match the commit SHA of the current update (`{update.CommitSha}`).
                """);
        }

        if (sourceDependency.Uri != update.SourceRepo)
        {
            configurationErrors.Add($"""
                #### {configurationErrors.Count + 1}. Source Uri Mismatch in `{VersionFiles.VersionDetailsXml}`
                - **Source Repository**: {update.SourceRepo}
                - **Error**: The Source Uri value `{sourceDependency.Uri}` found in `{VersionFiles.VersionDetailsXml}` does not match the source repository of the current update (`{update.SourceRepo}`).
                """);
        }
        return configurationErrors;
    }
}
