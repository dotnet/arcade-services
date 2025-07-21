// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;

namespace Maestro.MergePolicies;
public class VersionDetailsPropsMergePolicy : MergePolicy
{
    public override string Name => "VersionDetailsProps";

    public override string DisplayName => "Version Details Properties Merge Policy";

    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote remote)
    {
        if (pr.CodeFlowDirection != CodeFlowDirection.BackFlow)
        {
            // TODO: https://github.com/dotnet/arcade-services/issues/4998 Make the check work for forward flow PRs once we implement the issue
            return SucceedDecisively("Version Details Properties Merge Policy: Not a backflow PR");
        }

        try
        {
            XmlDocument versionDetailsProps;
            try
            {
                versionDetailsProps = DependencyFileManager.GetXmlDocument(await remote.GetFileContentsAsync(
                    VersionFiles.VersionDetailsProps,
                    pr.TargetRepoUrl,
                    pr.HeadBranch));
            }
            catch (DependencyFileNotFoundException)
            {
                // TODO: this should only be in for the transition period until we add VersionDetailsProps to all codeflow repos
                return SucceedDecisively("Version Details Properties Merge Policy: VersionDetailsProps file not found, skipping validation.");
            }

            var versionProps = DependencyFileManager.GetXmlDocument(await remote.GetFileContentsAsync(
                VersionFiles.VersionProps,
                pr.TargetRepoUrl,
                pr.HeadBranch));

            // Extract all properties from VersionDetailsProps
            var versionDetailsPropsProperties = ExtractPropertiesFromXml(versionDetailsProps);
            
            // Extract all properties from VersionProps
            var versionPropsProperties = ExtractPropertiesFromXml(versionProps);

            // Check if any properties from VersionDetailsProps exist in VersionProps
            var foundProperties = new List<string>();
            foreach (var versionDetailsPropsProperty in versionDetailsPropsProperties)
            {
                if (versionPropsProperties.Contains(versionDetailsPropsProperty))
                {
                    foundProperties.Add(versionDetailsPropsProperty);
                }
            }

            if (foundProperties.Count > 0)
            {
                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine("### ❌ Version Details Properties Validation Failed");
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("Properties from `VersionDetailsProps` should not be present in `VersionProps`. The following conflicting properties were found:");
                messageBuilder.AppendLine();
                
                foreach (var property in foundProperties)
                {
                    messageBuilder.AppendLine($"- `{property}`");
                }
                
                messageBuilder.AppendLine();
                messageBuilder.AppendLine("**Action Required:** Please remove these properties from `VersionProps` to ensure proper separation of concerns between the two files.");

                return FailDecisively(messageBuilder.ToString());
            }

            return SucceedDecisively("No properties from VersionDetailsProps are present in VersionProps");
        }
        catch (Exception ex)
        {
            return FailTransiently(
                "Failed to evaluate VersionDetailsProps merge policy",
                $"An error occurred while comparing VersionDetailsProps and VersionProps: {ex.Message}");
        }
    }

    private static HashSet<string> ExtractPropertiesFromXml(XmlDocument xmlDocument)
    {
        var properties = new HashSet<string>();
        
        // Get all PropertyGroup elements
        var propertyGroups = xmlDocument.GetElementsByTagName("PropertyGroup");
        
        foreach (XmlNode propertyGroup in propertyGroups)
        {
            foreach (XmlNode child in propertyGroup.ChildNodes)
            {
                // Skip comments and whitespace
                if (child.NodeType == XmlNodeType.Element)
                {
                    properties.Add(child.Name);
                }
            }
        }
        
        return properties;
    }
}

public class VersionDetailsPropsMergePolicyBuilder : IMergePolicyBuilder
{
    public string Name => MergePolicyConstants.VersionDetailsPropsMergePolicyName;

    public Task<IReadOnlyList<IMergePolicy>> BuildMergePoliciesAsync(MergePolicyProperties properties, PullRequestUpdateSummary pr)
    {
        IReadOnlyList<IMergePolicy> policies = new List<IMergePolicy> { new VersionDetailsPropsMergePolicy() };
        return Task.FromResult(policies);
    }
}
