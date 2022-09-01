// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

#nullable enable
namespace Microsoft.DotNet.DarcLib;

public interface IVersionDetailsParser
{
    IList<DependencyDetail> ParseVersionDetailsXml(string fileContents, bool includePinned = true);

    IList<DependencyDetail> ParseVersionDetailsXml(XmlDocument document, bool includePinned = true);
}

public class VersionDetailsParser : IVersionDetailsParser
{
    public IList<DependencyDetail> ParseVersionDetailsXml(string fileContents, bool includePinned = true)
    {
        XmlDocument document = GetXmlDocument(fileContents);
        return ParseVersionDetailsXml(document, includePinned: includePinned);
    }

    public IList<DependencyDetail> ParseVersionDetailsXml(XmlDocument document, bool includePinned = true)
    {
        XmlNodeList? dependencyNodes = document?.DocumentElement?.SelectNodes($"//{VersionFiles.DependencyElementName}");
        if (dependencyNodes == null)
        {
            throw new Exception($"There was an error while reading '{VersionFiles.VersionDetailsXml}' and it came back empty. " +
                $"Look for exceptions above.");
        }

        var dependencies = ParseDependencyDetails(dependencyNodes);
        return includePinned ? dependencies : dependencies.Where(d => !d.Pinned).ToList();
    }

    private static List<DependencyDetail> ParseDependencyDetails(XmlNodeList dependencies)
    {
        List<DependencyDetail> dependencyDetails = new List<DependencyDetail>();

        foreach (XmlNode dependency in dependencies)
        {
            if (dependency.NodeType == XmlNodeType.Comment || dependency.NodeType == XmlNodeType.Whitespace)
            {
                continue;
            }

            if (dependency.ParentNode is null)
            {
                throw new DarcException($"{VersionFiles.DependencyElementName} elements cannot be top-level; " +
                    $"they must belong to a group such as {VersionFiles.ProductDependencyElementName}");
            }

            if (dependency.Attributes is null)
            {
                throw new DarcException($"Dependencies cannot be top-level and must belong to a group such as {VersionFiles.ProductDependencyElementName}");
            }

            var type = dependency.ParentNode.Name switch
            {
                VersionFiles.ProductDependencyElementName => DependencyType.Product,
                VersionFiles.ToolsetDependencyElementName => DependencyType.Toolset,
                _ => throw new DarcException($"Unknown dependency type '{dependency.ParentNode.Name}'"),
            };

            // If the 'Pinned' attribute does not exist or if it is set to false we just not update it
            bool isPinned = ParseBooleanAttribute(dependency.Attributes, VersionFiles.PinnedAttributeName);

            XmlNode? sourceBuildNode = dependency.SelectSingleNode(VersionFiles.SourceBuildElementName)
                ?? dependency.SelectSingleNode(VersionFiles.SourceBuildOldElementName); // Workaround for https://github.com/dotnet/source-build/issues/2481

            SourceBuildInfo? sourceBuildInfo = null;
            if (sourceBuildNode is XmlElement sourceBuildElement)
            {
                string repoName = sourceBuildElement.Attributes[VersionFiles.RepoNameAttributeName]?.Value
                    ?? throw new DarcException($"{VersionFiles.RepoNameAttributeName} of {VersionFiles.SourceBuildElementName} " +
                                               $"null or empty in '{dependency.Attributes[VersionFiles.NameAttributeName]?.Value}'");

                sourceBuildInfo = new SourceBuildInfo
                {
                    RepoName = repoName,
                    ManagedOnly = ParseBooleanAttribute(sourceBuildElement.Attributes, VersionFiles.ManagedOnlyAttributeName),
                    TarballOnly = ParseBooleanAttribute(sourceBuildElement.Attributes, VersionFiles.TarballOnlyAttributeName),
                };
            }

            var dependencyDetail = new DependencyDetail
            {
                Name = dependency.Attributes[VersionFiles.NameAttributeName]?.Value?.Trim(),
                RepoUri = dependency.SelectSingleNode(VersionFiles.UriElementName)?.InnerText?.Trim(),
                Commit = dependency.SelectSingleNode(VersionFiles.ShaElementName)?.InnerText?.Trim(),
                Version = dependency.Attributes[VersionFiles.VersionAttributeName]?.Value?.Trim(),
                CoherentParentDependencyName = dependency.Attributes[VersionFiles.CoherentParentAttributeName]?.Value?.Trim(),
                Pinned = isPinned,
                Type = type,
                SourceBuild = sourceBuildInfo,
            };

            dependencyDetails.Add(dependencyDetail);
        }

        return dependencyDetails;
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

        document.LoadXml(fileContent);

        return document;
    }
}
