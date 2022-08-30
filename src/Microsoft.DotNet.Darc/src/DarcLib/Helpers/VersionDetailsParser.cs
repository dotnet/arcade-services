// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

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
        List<DependencyDetail> dependencyDetails = new List<DependencyDetail>();

        if (document != null)
        {
            BuildDependencies(document.DocumentElement.SelectNodes("//Dependency"));

            void BuildDependencies(XmlNodeList dependencies)
            {
                if (dependencies.Count > 0)
                {
                    foreach (XmlNode dependency in dependencies)
                    {
                        if (dependency.NodeType != XmlNodeType.Comment && dependency.NodeType != XmlNodeType.Whitespace)
                        {
                            DependencyType type;
                            switch (dependency.ParentNode.Name)
                            {
                                case "ProductDependencies":
                                    type = DependencyType.Product;
                                    break;
                                case "ToolsetDependencies":
                                    type = DependencyType.Toolset;
                                    break;
                                default:
                                    throw new DarcException($"Unknown dependency type '{dependency.ParentNode.Name}'");
                            }

                            bool isPinned = false;

                            // If the 'Pinned' attribute does not exist or if it is set to false we just not update it
                            if (dependency.Attributes[VersionFiles.PinnedAttributeName] != null)
                            {
                                if (!bool.TryParse(dependency.Attributes[VersionFiles.PinnedAttributeName].Value, out isPinned))
                                {
                                    throw new DarcException($"The '{VersionFiles.PinnedAttributeName}' attribute is set but the value " +
                                        $"'{dependency.Attributes[VersionFiles.PinnedAttributeName].Value}' " +
                                        $"is not a valid boolean...");
                                }
                            }

                            DependencyDetail dependencyDetail = new DependencyDetail
                            {
                                Name = dependency.Attributes[VersionFiles.NameAttributeName].Value?.Trim(),
                                RepoUri = dependency.SelectSingleNode(VersionFiles.UriElementName).InnerText?.Trim(),
                                Commit = dependency.SelectSingleNode(VersionFiles.ShaElementName)?.InnerText?.Trim(),
                                Version = dependency.Attributes[VersionFiles.VersionAttributeName].Value?.Trim(),
                                CoherentParentDependencyName = dependency.Attributes[VersionFiles.CoherentParentAttributeName]?.Value?.Trim(),
                                Pinned = isPinned,
                                Type = type
                            };

                            dependencyDetails.Add(dependencyDetail);
                        }
                    }
                }
            }
        }
        else
        {
            throw new Exception($"There was an error while reading '{VersionFiles.VersionDetailsXml}' and it came back empty. " +
                $"Look for exceptions above.");
        }

        if (includePinned)
        {
            return dependencyDetails;
        }

        return dependencyDetails.Where(d => !d.Pinned).ToList();
    }

    private static XmlDocument GetXmlDocument(string fileContent)
    {
        XmlDocument document = new XmlDocument
        {
            PreserveWhitespace = true
        };

        document.LoadXml(fileContent);

        return document;
    }
}
