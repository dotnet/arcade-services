// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.DarcLib;

/// <summary>
/// Extension of the GitFileManager that uses LibGit2Sharp to perform operations on local repositories.
/// </summary>
public class LibGit2FileManager : GitFileManager, ILibGit2FileManager
{
    private static readonly Dictionary<string, KnownDependencyType> _knownAssetNames = new()
    {
        { "Microsoft.DotNet.Arcade.Sdk", KnownDependencyType.GlobalJson },
        { "Microsoft.DotNet.Build.Tasks.SharedFramework.Sdk", KnownDependencyType.GlobalJson },
        { "Microsoft.DotNet.Helix.Sdk", KnownDependencyType.GlobalJson },
        { "Microsoft.DotNet.SharedFramework.Sdk", KnownDependencyType.GlobalJson },
        { "Microsoft.NET.SharedFramework.Sdk", KnownDependencyType.GlobalJson },
        { "Microsoft.DotNet.CMake.Sdk", KnownDependencyType.GlobalJson },
        { "dotnet", KnownDependencyType.GlobalJson },
    };

    private static readonly Dictionary<string, string> _sdkMapping = new()
    {
        { "Microsoft.DotNet.Arcade.Sdk", "msbuild-sdks" },
        { "Microsoft.DotNet.Build.Tasks.SharedFramework.Sdk", "msbuild-sdks" },
        { "Microsoft.DotNet.Helix.Sdk", "msbuild-sdks" },
        { "Microsoft.DotNet.SharedFramework.Sdk", "msbuild-sdks" },
        { "Microsoft.NET.SharedFramework.Sdk", "msbuild-sdks" },
        { "dotnet", "tools" },
    };

    private readonly ILocalLibGit2Client _localGitClient;
    private readonly ILogger _logger;

    public LibGit2FileManager(
        ILocalLibGit2Client localLibGit2Client,
        IVersionDetailsParser versionDetailsParser,
        ILogger logger)
        : base(localLibGit2Client, versionDetailsParser, logger)
    {
        _localGitClient = localLibGit2Client;
        _logger = logger;
    }

    public async Task AddDependencyAsync(
        DependencyDetail dependency,
        string repoUri,
        string branch)
    {
        var existingDependencies = await ParseVersionDetailsXmlAsync(repoUri, branch);
        if (existingDependencies.Any(dep => dep.Name.Equals(dependency.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DependencyException($"Dependency {dependency.Name} already exists in this repository");
        }

        if (_knownAssetNames.ContainsKey(dependency.Name))
        {
            if (!_sdkMapping.TryGetValue(dependency.Name, out string parent))
            {
                throw new Exception($"Dependency '{dependency.Name}' has no parent mapping defined.");
            }

            await AddDependencyToGlobalJson(
                repoUri,
                branch,
                parent,
                dependency.Name,
                dependency.Version);
        }

        await AddDependencyToVersionDetailsAsync(
            repoUri,
            branch,
            dependency);
    }

    public async Task AddDependencyToVersionDetailsAsync(
        string repo,
        string branch,
        DependencyDetail dependency)
    {
        XmlDocument versionDetails = await ReadVersionDetailsXmlAsync(repo, null);

        XmlNode newDependency = versionDetails.CreateElement(VersionDetailsParser.DependencyElementName);

        SetAttribute(versionDetails, newDependency, VersionDetailsParser.NameAttributeName, dependency.Name);
        SetAttribute(versionDetails, newDependency, VersionDetailsParser.VersionAttributeName, dependency.Version);

        // Only add the pinned attribute if the pinned option is set to true
        if (dependency.Pinned)
        {
            SetAttribute(versionDetails, newDependency, VersionDetailsParser.PinnedAttributeName, "True");
        }

        // Only add the coherent parent attribute if it is set
        if (!string.IsNullOrEmpty(dependency.CoherentParentDependencyName))
        {
            SetAttribute(versionDetails, newDependency, VersionDetailsParser.CoherentParentAttributeName, dependency.CoherentParentDependencyName);
        }

        SetElement(versionDetails, newDependency, VersionDetailsParser.UriElementName, dependency.RepoUri);
        SetElement(versionDetails, newDependency, VersionDetailsParser.ShaElementName, dependency.Commit);

        XmlNode dependenciesNode = versionDetails.SelectSingleNode($"//{dependency.Type}{VersionDetailsParser.DependenciesElementName}");
        if (dependenciesNode == null)
        {
            dependenciesNode = versionDetails.CreateElement($"{dependency.Type}{VersionDetailsParser.DependenciesElementName}");
            versionDetails.DocumentElement.AppendChild(dependenciesNode);
        }
        dependenciesNode.AppendChild(newDependency);

        // TODO: This should not be done here.  This should return some kind of generic file container to the caller,
        // who will gather up all updates and then call the git client to write the files all at once:
        // https://github.com/dotnet/arcade/issues/1095.  Today this is only called from the Local interface so
        // it's okay for now.
        var file = new GitFile(VersionFiles.VersionDetailsXml, versionDetails);
        await _localGitClient.CommitFilesAsync(new List<GitFile> { file }, repo, branch, $"Add {dependency} to " +
            $"'{VersionFiles.VersionDetailsXml}'");

        _logger.LogInformation(
            $"Dependency '{dependency.Name}' with version '{dependency.Version}' successfully added to " +
            $"'{VersionFiles.VersionDetailsXml}'");
    }

    public async Task AddDependencyToVersionsPropsAsync(string repo, string branch, DependencyDetail dependency)
    {
        XmlDocument versionProps = await ReadVersionPropsAsync(repo, null);
        string documentNamespaceUri = versionProps.DocumentElement.NamespaceURI;

        string packageVersionElementName = VersionFiles.GetVersionPropsPackageVersionElementName(dependency.Name);
        string packageVersionAlternateElementName = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(
            dependency.Name);

        // Attempt to find the element name or alternate element name under
        // the property group nodes
        XmlNode existingVersionNode = versionProps.DocumentElement.SelectSingleNode($"//*[local-name()='{packageVersionElementName}' and parent::PropertyGroup]");
        existingVersionNode ??= versionProps.DocumentElement.SelectSingleNode($"//*[local-name()='{packageVersionAlternateElementName}' and parent::PropertyGroup]");

        if (existingVersionNode != null)
        {
            existingVersionNode.InnerText = dependency.Version;
        }
        else
        {
            // Select elements by local name, since the Project (DocumentElement) element often has a default
            // xmlns set.
            XmlNodeList propertyGroupNodes = versionProps.DocumentElement.SelectNodes($"//*[local-name()='PropertyGroup']");

            bool addedPackageVersionElement = false;
            // There can be more than one property group.  Find the appropriate one containing an existing element of
            // the same type, and add it to the parent.
            foreach (XmlNode propertyGroupNode in propertyGroupNodes)
            {
                if (propertyGroupNode.HasChildNodes)
                {
                    foreach (XmlNode propertyNode in propertyGroupNode.ChildNodes)
                    {
                        if (!addedPackageVersionElement && propertyNode.Name.EndsWith(VersionDetailsParser.VersionPropsVersionElementSuffix))
                        {
                            XmlNode newPackageVersionElement = versionProps.CreateElement(
                                packageVersionElementName,
                                documentNamespaceUri);
                            newPackageVersionElement.InnerText = dependency.Version;

                            propertyGroupNode.AppendChild(newPackageVersionElement);
                            addedPackageVersionElement = true;
                            break;
                        }
                        // Test for alternate suffixes.  This will eventually be replaced by repo-level configuration:
                        // https://github.com/dotnet/arcade/issues/1095
                        else if (!addedPackageVersionElement && propertyNode.Name.EndsWith(
                                     VersionDetailsParser.VersionPropsAlternateVersionElementSuffix))
                        {
                            XmlNode newPackageVersionElement = versionProps.CreateElement(
                                packageVersionAlternateElementName,
                                documentNamespaceUri);
                            newPackageVersionElement.InnerText = dependency.Version;

                            propertyGroupNode.AppendChild(newPackageVersionElement);
                            addedPackageVersionElement = true;
                            break;
                        }
                    }
                }

                if (addedPackageVersionElement)
                {
                    break;
                }
            }

            // Add the property groups if none were present
            if (!addedPackageVersionElement)
            {
                // If the repository doesn't have any package version element, then
                // use the non-alternate element name.
                XmlNode newPackageVersionElement = versionProps.CreateElement(packageVersionElementName, documentNamespaceUri);
                newPackageVersionElement.InnerText = dependency.Version;

                XmlNode propertyGroupElement = versionProps.CreateElement("PropertyGroup", documentNamespaceUri);
                XmlNode propertyGroupCommentElement = versionProps.CreateComment("Package versions");
                versionProps.DocumentElement.AppendChild(propertyGroupCommentElement);
                versionProps.DocumentElement.AppendChild(propertyGroupElement);
                propertyGroupElement.AppendChild(newPackageVersionElement);
            }
        }

        // TODO: This should not be done here.  This should return some kind of generic file container to the caller,
        // who will gather up all updates and then call the git client to write the files all at once:
        // https://github.com/dotnet/arcade/issues/1095.  Today this is only called from the Local interface so
        // it's okay for now.
        var file = new GitFile(VersionFiles.VersionProps, versionProps);
        await _localGitClient.CommitFilesAsync(new List<GitFile> { file }, repo, branch, $"Add {dependency} to " +
            $"'{VersionFiles.VersionProps}'");

        _logger.LogInformation(
            $"Dependency '{dependency.Name}' with version '{dependency.Version}' successfully added to " +
            $"'{VersionFiles.VersionProps}'");
    }

    public async Task AddDependencyToGlobalJson(
        string repo,
        string branch,
        string parentField,
        string dependencyName,
        string version)
    {
        JToken versionProperty = new JProperty(dependencyName, version);
        JObject globalJson = await ReadGlobalJsonAsync(repo, branch);
        JToken parent = globalJson[parentField];

        if (parent != null)
        {
            parent.Last.AddAfterSelf(versionProperty);
        }
        else
        {
            globalJson.Add(new JProperty(parentField, new JObject(versionProperty)));
        }

        var file = new GitFile(VersionFiles.GlobalJson, globalJson);
        await _localGitClient.CommitFilesAsync(
            new List<GitFile> { file },
            repo,
            branch,
            $"Add {dependencyName} to '{VersionFiles.GlobalJson}'");

        _logger.LogInformation($"Dependency '{dependencyName}' with version '{version}' successfully added to global.json");
    }
}
