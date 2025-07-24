// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;

#nullable enable
namespace Maestro.MergePolicies;
public class VersionDetailsPropsMergePolicy : MergePolicy
{
    public override string Name => "VersionDetailsProps";

    public override string DisplayName => $"{Constants.VersionDetailsProps} Validation Merge Policy";

    public override async Task<MergePolicyEvaluationResult> EvaluateAsync(PullRequestUpdateSummary pr, IRemote remote)
    {
        if (pr.CodeFlowDirection == CodeFlowDirection.ForwardFlow)
        {
            // TODO: https://github.com/dotnet/arcade-services/issues/4998 Make the check work for forward flow PRs once we implement the issue
            return SucceedDecisively($"{DisplayName}: doesn't apply to forward flow PRs yet");
        }

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
            if (pr.CodeFlowDirection == CodeFlowDirection.None)
            {
                // If a dependency flow PR doesn't have the VersionDetailsProps file, it just means that the repo doesn't use it
                return SucceedDecisively($"{DisplayName}: {Constants.VersionDetailsProps} file not found, skipping validation.");
            }
            else
            {
                return FailDecisively($"{DisplayName}: {Constants.VersionDetailsProps} file must exist in all VMR repos");
            }
        }
        catch (XmlException)
        {
            return FailDecisively($"Failed to parse {Constants.VersionDetailsProps}",
                $"The {VersionFiles.VersionDetailsProps} file is not a valid XML document. Please ensure it is well-formed.");
        }
        catch
        {
            return FailTransiently($"Failed to evaluate {DisplayName}",
                $"An error occurred while trying to read the {VersionFiles.VersionDetailsProps} file");
        }

        try
        {
            var versionProps = DependencyFileManager.GetXmlDocument(await remote.GetFileContentsAsync(
                VersionFiles.VersionProps,
                pr.TargetRepoUrl,
                pr.HeadBranch));

            var versionDetailsPropsProperties = ExtractPropertiesFromXml(versionDetailsProps);
            
            var versionPropsProperties = ExtractPropertiesFromXml(versionProps);

            // Check if any properties from VersionDetailsProps exist in VersionsProps
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
                str.AppendLine($"Properties from `{Constants.VersionDetailsProps}` should not be present in `{Constants.VersionsProps}`.");
                str.AppendLine("The following conflicting properties were found:");
                foreach (var property in foundProperties)
                {
                    str.AppendLine($"- `{property}`");
                }
                str.AppendLine($"**Action Required:** Please remove these properties from `{Constants.VersionsProps}` to ensure proper separation of concerns between the two files.");
                return FailDecisively(
                    $"#### ❌ {DisplayName}: Validation Failed",
                    str.ToString());
            }

            // Check if VersionProps contains the required import statement
            var hasImport = CheckForVersionDetailsPropsImport(versionProps);
            if (!hasImport)
            {
                return FailDecisively(
                    $"#### ❌ {DisplayName} Validation Failed",
                    $"""
                    The `VersionProps` file is missing the required import statement for `{Constants.VersionDetailsProps}`.
                    **Action Required:** Please add the following import statement at the beginning of your `VersionProps` file:
                    ```xml
                    <Import Project="{Constants.VersionDetailsProps}" Condition="Exists('{Constants.VersionDetailsProps}')" />
                    ```
                    """);
            }

            var versionDetailsXml = DependencyFileManager.GetXmlDocument(await remote.GetFileContentsAsync(
                VersionFiles.VersionDetailsXml,
                pr.TargetRepoUrl,
                pr.HeadBranch));

            var versionDetailsParser = new VersionDetailsParser();
            var versionDetails = versionDetailsParser.ParseVersionDetailsXml(versionDetailsXml, includePinned: true);

            var (missingProperties, orphanedProperties) = CheckDependencyPropertyMapping(versionDetails.Dependencies, versionDetailsPropsProperties);
            if (missingProperties.Count > 0 || orphanedProperties.Count > 0)
            {
                StringBuilder str = new();
                str.AppendLine($"There is a mismatch between dependencies in `{Constants.VersionDetailsXml}` and properties in `{Constants.VersionDetailsProps}`.");
                str.AppendLine();
                
                    if (missingProperties.Count > 0)
                    {
                        str.AppendLine($"**Missing Properties:** The following dependencies are missing corresponding properties in `{Constants.VersionDetailsProps}`:");
                        foreach (var (expectedPropertyName, version) in missingProperties)
                        {
                            str.AppendLine($"- Add `<{expectedPropertyName}>{version}</{expectedPropertyName}>`");
                        }
                        str.AppendLine();
                    }                if (orphanedProperties.Count > 0)
                {
                    str.AppendLine($"**Orphaned Properties:** The following properties in `{Constants.VersionDetailsProps}` do not correspond to any dependency:");
                    foreach (var orphanedProperty in orphanedProperties)
                    {
                        str.AppendLine($"- Remove `<{orphanedProperty}>...</{orphanedProperty}>`");
                    }
                    str.AppendLine();
                }
                
                return FailDecisively(
                    $"#### ❌ {DisplayName}: Validation Failed",
                    str.ToString());
            }

            return SucceedDecisively($"{DisplayName}: All validation checks passed");
        }
        catch
        {
            return FailTransiently(
                $"Failed to evaluate {DisplayName}",
                $"An error occurred while evaluating {DisplayName}");
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
                if (projectValue.Contains(Constants.VersionDetailsProps))
                {
                    return true;
                }
            }
        }
        
        return false;
    }

    private static (List<(string ExpectedPropertyName, string Version)> MissingProperties, List<string> OrphanedProperties) 
        CheckDependencyPropertyMapping(IReadOnlyCollection<DependencyDetail> dependencies, HashSet<string> versionDetailsPropsProperties)
    {
        var missingProperties = new List<(string ExpectedPropertyName, string Version)>();
        var orphanedProperties = new List<string>();
        
        // Check for missing properties (dependencies without corresponding properties)
        foreach (var dependency in dependencies)
        {
            var expectedPropertyName = VersionFiles.GetVersionPropsPackageVersionElementName(dependency.Name);
            
            if (!versionDetailsPropsProperties.Contains(expectedPropertyName))
            {
                missingProperties.Add((expectedPropertyName, dependency.Version ?? "VERSION"));
            }
        }
        
        // Check for orphaned properties (properties that don't correspond to any dependency)
        var expectedProperties = dependencies.Select(d => VersionFiles.GetVersionPropsPackageVersionElementName(d.Name)).ToHashSet();
        orphanedProperties.AddRange(versionDetailsPropsProperties.Where(prop => !expectedProperties.Contains(prop)));
        
        return (missingProperties, orphanedProperties);
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
