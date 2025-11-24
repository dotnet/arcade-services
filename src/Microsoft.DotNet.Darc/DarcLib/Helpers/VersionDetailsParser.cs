// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

public interface IVersionDetailsParser
{
    VersionDetails ParseVersionDetailsFile(string path, bool includePinned = true);

    VersionDetails ParseVersionDetailsXml(string fileContents, bool includePinned = true);

    VersionDetails ParseVersionDetailsXml(XmlDocument document, bool includePinned = true);
}

public class VersionDetailsParser : IVersionDetailsParser
{
    // Version.Details.xml schema
    public const string VersionPropsVersionElementSuffix = "PackageVersion";
    public const string VersionPropsAlternateVersionElementSuffix = "Version";
    public const string ShaElementName = "Sha";
    public const string UriElementName = "Uri";
    public const string MappingElementName = "Mapping";
    public const string BarIdElementName = "BarId";
    public const string DependencyElementName = "Dependency";
    public const string DependenciesElementName = "Dependencies";
    public const string NameAttributeName = "Name";
    public const string VersionAttributeName = "Version";
    public const string CoherentParentAttributeName = "CoherentParentDependency";
    public const string ProductDependencyElementName = "ProductDependencies";
    public const string ToolsetDependencyElementName = "ToolsetDependencies";
    public const string PinnedAttributeName = "Pinned";
    public const string SkipPropertyAttributeName = "SkipProperty";
    public const string AddElement = "add";
    public const string ClearElement = "clear";
    public const string KeyAttributeName = "key";
    public const string ValueAttributeName = "value";
    public const string SourceBuildElementName = "SourceBuild";
    public const string SourceBuildOldElementName = "SourceBuildTarball";
    public const string RepoNameAttributeName = "RepoName";
    public const string ManagedOnlyAttributeName = "ManagedOnly";
    public const string TarballOnlyAttributeName = "TarballOnly";
    public const string SourceElementName = "Source";

    public VersionDetails ParseVersionDetailsFile(string path, bool includePinned = true)
    {
        var content = File.ReadAllText(path);
        return ParseVersionDetailsXml(content, includePinned: includePinned);
    }

    public VersionDetails ParseVersionDetailsXml(string fileContents, bool includePinned = true)
    {
        XmlDocument document = GetXmlDocument(fileContents);
        return ParseVersionDetailsXml(document, includePinned: includePinned);
    }

    public VersionDetails ParseVersionDetailsXml(XmlDocument document, bool includePinned = true)
    {
        XmlNodeList? dependencyNodes = (document?.DocumentElement?.SelectNodes($"//{DependencyElementName}"))
            ?? throw new Exception($"There was an error while reading '{VersionFiles.VersionDetailsXml}' and it came back empty. " +
                $"Look for exceptions above.");

        List<DependencyDetail> dependencies = ParseDependencyDetails(dependencyNodes);
        dependencies = includePinned ? dependencies : [.. dependencies.Where(d => !d.Pinned)];

        // Parse the VMR codeflow if it exists
        SourceDependency? vmrCodeflow = ParseSourceSection(document?.DocumentElement?.SelectSingleNode($"//{SourceElementName}"));

        return new VersionDetails(dependencies, vmrCodeflow);
    }

    private static List<DependencyDetail> ParseDependencyDetails(XmlNodeList dependencies)
    {
        List<DependencyDetail> dependencyDetails = [];

        foreach (XmlNode dependency in dependencies)
        {
            if (dependency.NodeType == XmlNodeType.Comment || dependency.NodeType == XmlNodeType.Whitespace)
            {
                continue;
            }

            if (dependency.ParentNode is null)
            {
                throw new DarcException($"{DependencyElementName} elements cannot be top-level; " +
                    $"they must belong to a group such as {ProductDependencyElementName}");
            }

            if (dependency.Attributes is null)
            {
                throw new DarcException($"Dependencies cannot be top-level and must belong to a group such as {ProductDependencyElementName}");
            }

            var type = dependency.ParentNode.Name switch
            {
                ProductDependencyElementName => DependencyType.Product,
                ToolsetDependencyElementName => DependencyType.Toolset,
                _ => throw new DarcException($"Unknown dependency type '{dependency.ParentNode.Name}'"),
            };

            // If the 'Pinned' attribute does not exist or if it is set to false we just not update it
            var isPinned = ParseBooleanAttribute(dependency.Attributes, PinnedAttributeName);

            var skipProperty = ParseBooleanAttribute(dependency.Attributes, SkipPropertyAttributeName);

            XmlNode? sourceBuildNode = dependency.SelectSingleNode(SourceBuildElementName)
                ?? dependency.SelectSingleNode(SourceBuildOldElementName); // Workaround for https://github.com/dotnet/source-build/issues/2481

            SourceBuildInfo? sourceBuildInfo = null;
            if (sourceBuildNode is XmlElement sourceBuildElement)
            {
                var repoName = sourceBuildElement.Attributes[RepoNameAttributeName]?.Value
                    ?? throw new DarcException($"{RepoNameAttributeName} of {SourceBuildElementName} " +
                                               $"null or empty in '{dependency.Attributes[NameAttributeName]?.Value}'");

                sourceBuildInfo = new SourceBuildInfo
                {
                    RepoName = repoName,
                    ManagedOnly = ParseBooleanAttribute(sourceBuildElement.Attributes, ManagedOnlyAttributeName),
                    TarballOnly = ParseBooleanAttribute(sourceBuildElement.Attributes, TarballOnlyAttributeName),
                };
            }

            var dependencyDetail = new DependencyDetail
            {
                Name = dependency.Attributes[NameAttributeName]?.Value?.Trim(),
                RepoUri = dependency.SelectSingleNode(UriElementName)?.InnerText?.Trim(),
                Commit = dependency.SelectSingleNode(ShaElementName)?.InnerText?.Trim(),
                Version = dependency.Attributes[VersionAttributeName]?.Value?.Trim(),
                CoherentParentDependencyName = dependency.Attributes[CoherentParentAttributeName]?.Value?.Trim(),
                Pinned = isPinned,
                SkipProperty = skipProperty,
                Type = type,
                SourceBuild = sourceBuildInfo,
            };

            dependencyDetails.Add(dependencyDetail);
        }

        return dependencyDetails;
    }

    private static SourceDependency? ParseSourceSection(XmlNode? sourceNode)
    {
        if (sourceNode is null)
        {
            return null;
        }

        //todo turning the exceptions thrown below into more specific parsing exceptions would enable more robust
        //exception handling in parts of the server that use this method
        var uri = sourceNode.Attributes?[UriElementName]?.Value?.Trim()
            ?? throw new DarcException($"The XML tag `{SourceElementName}` does not contain a value for attribute `{UriElementName}`");

        var sha = sourceNode.Attributes[ShaElementName]?.Value?.Trim()
            ?? throw new DarcException($"The XML tag `{SourceElementName}` does not contain a value for attribute `{ShaElementName}`");

        var mapping = sourceNode.Attributes[MappingElementName]?.Value?.Trim()
            ?? throw new DarcException($"The XML tag `{SourceElementName}` does not contain a value for attribute `{MappingElementName}`");

        var barIdAttribute = sourceNode.Attributes[BarIdElementName]?.Value?.Trim();
        _ = int.TryParse(barIdAttribute, out int barId);

        return new SourceDependency(uri, mapping, sha, barId);
    }

    private static bool ParseBooleanAttribute(XmlAttributeCollection attributes, string attributeName)
    {
        var result = false;
        XmlAttribute? attribute = attributes[attributeName];
        if (attribute is not null && !bool.TryParse(attribute.Value, out result))
        {
            throw new DarcException($"The '{attributeName}' attribute is set but the value " +
                $"'{attribute.Value}' is not a valid boolean...");
        }

        return result;
    }

    private static XmlDocument GetXmlDocument(string fileContent)
    {
        var document = new XmlDocument
        {
            PreserveWhitespace = true
        };

        // Remove BOM character if present
        if (fileContent.StartsWith("∩╗┐"))
        {
            fileContent = fileContent.Substring(3);
        }

        document.LoadXml(fileContent);

        return document;
    }

    public static string SerializeSourceDependency(SourceDependency sourceDependency)
    {
        return $"<{SourceElementName} " +
            $"{UriElementName}=\"{sourceDependency.Uri}\" " +
            $"{MappingElementName}=\"{sourceDependency.Mapping}\" " +
            $"{ShaElementName}=\"{sourceDependency.Sha}\" " +
            $"{BarIdElementName}=\"{sourceDependency.BarId}\" />";
    }
}
