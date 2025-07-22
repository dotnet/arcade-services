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

#nullable enable
namespace Maestro.MergePolicies;
public class VersionDetailsPropsMergePolicy : MergePolicy
{
    public override string Name => "VersionDetailsProps";

    public override string DisplayName => "Version Details Properties Merge Policy";

    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote remote)
    {
        if (pr.CodeFlowDirection == CodeFlowDirection.ForwardFlow)
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
                StringBuilder str = new();
                str.AppendLine("Properties from `VersionDetailsProps` should not be present in `VersionProps`.");
                str.AppendLine("The following conflicting properties were found:");
                foreach (var property in foundProperties)
                {
                    str.AppendLine($"- `{property}`");
                }
                str.AppendLine("**Action Required:** Please remove these properties from `VersionProps` to ensure proper separation of concerns between the two files.");
                return FailDecisively(
                    "#### ❌ Version Details Properties Validation Failed",
                    str.ToString());
            }

            // Check if VersionProps contains the required import statement
            var hasImport = CheckForVersionDetailsPropsImport(versionProps);
            if (!hasImport)
            {
                return FailDecisively(
                    "#### ❌ Version Details Properties Validation Failed",
                    """
                    The `VersionProps` file is missing the required import statement for `Version.Details.props`.
                    **Action Required:** Please add the following import statement at the beginning of your `VersionProps` file:
                    ```xml
                    <Import Project="Version.Details.props" Condition="Exists('Version.Details.props')" />
                    ```
                    """);
            }

            return SucceedDecisively("No properties from VersionDetailsProps are present in VersionProps and required import statement is present");
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

    private static bool CheckForVersionDetailsPropsImport(XmlDocument xmlDocument)
    {
        // Get all Import elements
        var importElements = xmlDocument.GetElementsByTagName("Import");
        
        foreach (XmlNode importElement in importElements)
        {
            var projectAttribute = importElement.Attributes?["Project"];
            if (projectAttribute != null)
            {
                var projectValue = projectAttribute.Value;
                // Check for various forms of the import (with or without path, with or without condition)
                if (projectValue.Contains("Version.Details.props"))
                {
                    return true;
                }
            }
        }
        
        return false;
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
