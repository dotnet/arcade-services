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
        XmlNodeList? dependencyNodes = document?.DocumentElement?.SelectNodes("//Dependency");
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

            var type = dependency.ParentNode!.Name switch
            {
                "ProductDependencies" => DependencyType.Product,
                "ToolsetDependencies" => DependencyType.Toolset,
                _ => throw new DarcException($"Unknown dependency type '{dependency.ParentNode.Name}'"),
            };

            bool isPinned = false;

            // If the 'Pinned' attribute does not exist or if it is set to false we just not update it
            XmlAttribute? isPinnedAttribute = dependency.Attributes![VersionFiles.PinnedAttributeName];
            if (isPinnedAttribute != null)
            {
                if (!bool.TryParse(isPinnedAttribute.Value, out isPinned))
                {
                    throw new DarcException($"The '{VersionFiles.PinnedAttributeName}' attribute is set but the value " +
                        $"'{isPinnedAttribute.Value}' is not a valid boolean...");
                }
            }

            SourceBuildInfo? sourceBuildInfo = null;

            XmlNode? sourceBuildNode = dependency.SelectSingleNode(VersionFiles.SourceBuildElementName)
                ?? dependency.SelectSingleNode(VersionFiles.SourceBuildOldElementName); // Workaround for https://github.com/dotnet/source-build/issues/2481

            if (sourceBuildNode is XmlElement sourceBuildElement)
            {
                string repoName = sourceBuildElement.Attributes[VersionFiles.RepoNameAttributeName]?.Value
                    ?? throw new DarcException($"{VersionFiles.RepoNameAttributeName} of {VersionFiles.SourceBuildElementName} " +
                                               $"null or empty in '{dependency.Attributes[VersionFiles.NameAttributeName]?.Value}'");

                bool managedOnly = false;
                XmlAttribute? managedOnlyAttribute = sourceBuildElement.Attributes[VersionFiles.ManagedOnlyAttributeName];
                if (managedOnlyAttribute is not null && !bool.TryParse(managedOnlyAttribute.Value, out managedOnly))
                {
                    throw new DarcException($"The '{VersionFiles.ManagedOnlyAttributeName}' attribute is set but the value " +
                        $"'{managedOnlyAttribute.Value}' is not a valid boolean...");
                }

                sourceBuildInfo = new SourceBuildInfo
                {
                    RepoName = repoName,
                    ManagedOnly = managedOnly,
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
