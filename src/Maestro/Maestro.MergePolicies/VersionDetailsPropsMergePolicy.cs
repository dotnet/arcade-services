// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Maestro.MergePolicyEvaluation;
using Microsoft.Build.Construction;
using Microsoft.Build.Exceptions;
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
            // TODO: https://github.com/dotnet/arcade-services/issues/5092 Also run it for dependency flow subscriptions
            return SucceedDecisively($"{DisplayName}: doesn't apply to this subscription");
        }

        ProjectRootElement versionDetailsProps;
        try
        {
            var versionDetailsPropsContent = await remote.GetFileContentsAsync(
                VersionFiles.VersionDetailsProps,
                pr.TargetRepoUrl,
                pr.HeadBranch);
            versionDetailsProps = ProjectRootElement.Create(
                XmlReader.Create(new StringReader(versionDetailsPropsContent)));
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
        catch (InvalidProjectFileException)
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
            var versionsPropsContent = await remote.GetFileContentsAsync(
                VersionFiles.VersionsProps,
                pr.TargetRepoUrl,
                pr.HeadBranch);
            var versionsProps = ProjectRootElement.Create(
                XmlReader.Create(new StringReader(versionsPropsContent)));

            var versionDetailsPropsProperties = ExtractNonConditionalNonEmptyProperties(versionDetailsProps);
            
            var versionPropsDictionary = ExtractNonConditionalNonEmptyProperties(versionsProps);

            // Check if any properties from VersionDetailsProps exist in VersionsProps
            var foundProperties = versionDetailsPropsProperties
                .Intersect(versionPropsDictionary)
                .ToList();
            bool versionDetailPropsImported = CheckForVersionDetailsPropsImport(versionsProps);

            if (foundProperties.Count > 0 || !versionDetailPropsImported)
            {
                StringBuilder str = new();
                str.AppendLine($"Validation issues found with `{Constants.VersionsProps}` file:");
                str.AppendLine();
                
                if (foundProperties.Count > 0)
                {
                    str.AppendLine($"**Conflicting Properties:** Properties from `{Constants.VersionDetailsProps}` should not be present in `{Constants.VersionsProps}`.");
                    str.AppendLine("The following conflicting properties were found:");
                    foreach (var property in foundProperties)
                    {
                        str.AppendLine($"- `{property}`");
                    }
                    str.AppendLine();
                }
                
                if (!versionDetailPropsImported)
                {
                    str.AppendLine($"**Missing Import:** The `{Constants.VersionsProps}` file is missing the required import statement for `{Constants.VersionDetailsProps}`.");
                    str.AppendLine();
                }
                
                str.AppendLine("**Action Required:**");
                if (foundProperties.Count > 0)
                {
                    str.AppendLine($"- Remove the conflicting properties from `{Constants.VersionsProps}` to ensure proper separation of concerns between the two files.");
                }
                if (!versionDetailPropsImported)
                {
                    str.AppendLine($"- Add the following import statement at the beginning of your `{Constants.VersionsProps}` file:");
                    str.AppendLine("  ```xml");
                    str.AppendLine($"  <Import Project=\"{Constants.VersionDetailsProps}\" Condition=\"Exists('{Constants.VersionDetailsProps}')\" />");
                    str.AppendLine("  ```");
                }
                
                return FailDecisively(
                    $"#### ❌ {DisplayName}: Validation Failed",
                    str.ToString());
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

    private static HashSet<string> ExtractNonConditionalNonEmptyProperties(ProjectRootElement versionsProps)
    {
        HashSet<string> nonConditionalProperties = [];
        foreach (var propertyGroup in versionsProps.PropertyGroups)
        {
            if (!string.IsNullOrEmpty(propertyGroup.Condition))
            {
                // Skip conditional property groups
                continue;
            }
            foreach (var property in propertyGroup.Properties)
            {
                if (!string.IsNullOrEmpty(property.Condition))
                {
                    // Skip conditional properties
                    continue;
                }
                if (!string.IsNullOrEmpty(property.Value))
                {
                    nonConditionalProperties.Add(property.Name);
                }
            }
        }

        return nonConditionalProperties;
    }

    private static bool CheckForVersionDetailsPropsImport(ProjectRootElement versionsProps) =>
        versionsProps.Imports.Any(import =>
            import.Project.Equals(Constants.VersionDetailsProps) && import.Condition == $"Exists('{Constants.VersionDetailsProps}')");

    private static (List<(string ExpectedPropertyName, string Version)> MissingProperties, List<string> OrphanedProperties) 
        CheckDependencyPropertyMapping(IReadOnlyCollection<DependencyDetail> dependencies, HashSet<string> versionDetailsPropsProperties)
    {
        var missingProperties = new List<(string ExpectedPropertyName, string Version)>();
        var orphanedProperties = new List<string>();
        
        // Check for missing properties (dependencies without corresponding properties)
        foreach (var dependency in dependencies)
        {
            var expectedPropertyName = VersionFiles.GetVersionPropsPackageVersionElementName(dependency.Name);
            var alternateExpectedPropertyName = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(dependency.Name);

            if (!versionDetailsPropsProperties.Contains(expectedPropertyName))
            {
                missingProperties.Add((expectedPropertyName, dependency.Version ?? "VERSION"));
            }
            if (!versionDetailsPropsProperties.Contains(alternateExpectedPropertyName))
            {
                missingProperties.Add((alternateExpectedPropertyName, $"$({expectedPropertyName})"));
            }
        }
        
        // Check for orphaned properties (properties that don't correspond to any dependency)
        var expectedProperties = dependencies
            .Select(d => VersionFiles.GetVersionPropsPackageVersionElementName(d.Name))
            .Concat(dependencies
                .Select(d => VersionFiles.GetVersionPropsAlternatePackageVersionElementName(d.Name)))
            .ToHashSet();
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
