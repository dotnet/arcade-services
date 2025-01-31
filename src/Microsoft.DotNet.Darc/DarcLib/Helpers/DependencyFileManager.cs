// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;

namespace Microsoft.DotNet.DarcLib.Helpers;

public class DependencyFileManager : IDependencyFileManager
{
    public const string ArcadeSdkPackageName = "Microsoft.DotNet.Arcade.Sdk";

    private static readonly ImmutableDictionary<string, KnownDependencyType> _knownAssetNames = new Dictionary<string, KnownDependencyType>()
    {
        { ArcadeSdkPackageName, KnownDependencyType.GlobalJson },
        { "Microsoft.DotNet.Build.Tasks.SharedFramework.Sdk", KnownDependencyType.GlobalJson },
        { "Microsoft.DotNet.Helix.Sdk", KnownDependencyType.GlobalJson },
        { "Microsoft.DotNet.SharedFramework.Sdk", KnownDependencyType.GlobalJson },
        { "Microsoft.NET.SharedFramework.Sdk", KnownDependencyType.GlobalJson },
        { "Microsoft.DotNet.CMake.Sdk", KnownDependencyType.GlobalJson },
        { "dotnet", KnownDependencyType.GlobalJson },
    }.ToImmutableDictionary();

    private static readonly ImmutableDictionary<string, string> _sdkMapping = new Dictionary<string, string>()
    {
        { ArcadeSdkPackageName, "msbuild-sdks" },
        { "Microsoft.DotNet.Build.Tasks.SharedFramework.Sdk", "msbuild-sdks" },
        { "Microsoft.DotNet.Helix.Sdk", "msbuild-sdks" },
        { "Microsoft.DotNet.SharedFramework.Sdk", "msbuild-sdks" },
        { "Microsoft.NET.SharedFramework.Sdk", "msbuild-sdks" },
        { "dotnet", "tools" },
    }.ToImmutableDictionary();

    private IGitRepo GetGitClient(string repoUri) => _gitClientFactory(repoUri);

    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly Func<string, IGitRepo> _gitClientFactory;
    private readonly ILogger _logger;

    private const string MaestroBeginComment =
        "Begin: Package sources managed by Dependency Flow automation. Do not edit the sources below.";

    private const string MaestroEndComment =
        "End: Package sources managed by Dependency Flow automation. Do not edit the sources above.";

    private const string MaestroRepoSpecificBeginComment = "  Begin: Package sources from";
    private const string MaestroRepoSpecificEndComment = "  End: Package sources from";

    public DependencyFileManager(
        IGitRepo gitClient,
        IVersionDetailsParser versionDetailsParser,
        ILogger logger)
    {
        _gitClientFactory = _ => gitClient;
        _versionDetailsParser = versionDetailsParser;
        _logger = logger;
    }

    public DependencyFileManager(
        IGitRepoFactory gitClientFactory,
        IVersionDetailsParser versionDetailsParser,
        ILogger logger)
    {
        _gitClientFactory = gitClientFactory.CreateClient;
        _versionDetailsParser = versionDetailsParser;
        _logger = logger;
    }

    public static ImmutableHashSet<string> DependencyFiles { get; } = new HashSet<string>()
    {
        VersionFiles.VersionDetailsXml,
        VersionFiles.VersionProps,
        VersionFiles.GlobalJson,
        VersionFiles.DotnetToolsConfigJson
    }.ToImmutableHashSet();

    public async Task<XmlDocument> ReadVersionDetailsXmlAsync(string repoUri, string branch)
    {
        return await ReadXmlFileAsync(VersionFiles.VersionDetailsXml, repoUri, branch);
    }

    public async Task<XmlDocument> ReadVersionPropsAsync(string repoUri, string branch)
    {
        return await ReadXmlFileAsync(VersionFiles.VersionProps, repoUri, branch);
    }

    public async Task<JObject> ReadGlobalJsonAsync(string repoUri, string branch, bool repoIsVmr)
    {
        var path = repoIsVmr ?
                VmrInfo.ArcadeRepoDir / VersionFiles.GlobalJson :
                VersionFiles.GlobalJson;

        _logger.LogInformation("Reading '{filePath}' in repo '{repoUri}' and branch '{branch}'...",
            path,
            repoUri,
            branch);

        var fileContent = await GetGitClient(repoUri).GetFileContentsAsync(path, repoUri, branch);

        return JObject.Parse(fileContent);
    }

    public async Task<JObject> ReadDotNetToolsConfigJsonAsync(string repoUri, string branch, bool repoIsVmr)
    {
        var path = repoIsVmr ?
                VmrInfo.ArcadeRepoDir / VersionFiles.DotnetToolsConfigJson :
                VersionFiles.DotnetToolsConfigJson;

        _logger.LogInformation("Reading '{filePath}' in repo '{repoUri}' and branch '{branch}'...",
            path,
            repoUri,
            branch);

        try
        {
            var fileContent = await GetGitClient(repoUri).GetFileContentsAsync(path, repoUri, branch);
            return JObject.Parse(fileContent);
        }
        catch (DependencyFileNotFoundException)
        {
            // Not exceptional: just means this repo doesn't have a .config/dotnet-tools.json, we'll skip the update.
            return null;
        }
    }


    /// <summary>
    /// Get the tools.dotnet section of the global.json from a target repo URI
    /// </summary>
    /// <param name="repoUri">repo to get the version from</param>
    /// <param name="commit">commit sha to query</param>
    public async Task<SemanticVersion> ReadToolsDotnetVersionAsync(string repoUri, string commit, bool repoIsVmr)
    {
        JObject globalJson = await ReadGlobalJsonAsync(repoUri, commit, repoIsVmr);
        JToken dotnet = globalJson.SelectToken("tools.dotnet", true);

        _logger.LogInformation("Reading dotnet version from global.json succeeded!");

        if (!SemanticVersion.TryParse(dotnet.ToString(), out SemanticVersion dotnetVersion))
        {
            _logger.LogError($"Failed to parse dotnet version from global.json from repo: {repoUri} at commit {commit}. Version: {dotnet}");
        }

        return dotnetVersion;
    }

    public async Task<XmlDocument> ReadNugetConfigAsync(string repoUri, string branch)
    {
        return await ReadXmlFileAsync(VersionFiles.NugetConfig, repoUri, branch);
    }

    public async Task<VersionDetails> ParseVersionDetailsXmlAsync(string repoUri, string branch, bool includePinned = true)
    {
        _logger.LogInformation(
            $"Getting a collection of dependencies from '{VersionFiles.VersionDetailsXml}' in repo '{repoUri}'" +
            (!string.IsNullOrEmpty(branch) ? $" and branch '{branch}'" : string.Empty) + "...");

        XmlDocument document = await ReadVersionDetailsXmlAsync(repoUri, branch);

        return _versionDetailsParser.ParseVersionDetailsXml(document, includePinned);
    }

    /// <summary>
    /// Add a new dependency to the repository
    /// </summary>
    /// <param name="dependency">Dependency to add.</param>
    /// <param name="repoUri">Repository URI to add the dependency to.</param>
    /// <param name="branch">Branch to add the dependency to.</param>
    /// <returns>Async task.</returns>
    public async Task AddDependencyAsync(
        DependencyDetail dependency,
        string repoUri,
        string branch)
    {
        // The Add Dependency operation doesn't support adding dependencies to VMR src/... folders
        bool repoIsVmr = false;
        var versionDetails = await ParseVersionDetailsXmlAsync(repoUri, branch);
        var existingDependencies = versionDetails.Dependencies;
        if (existingDependencies.Any(dep => dep.Name.Equals(dependency.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DependencyException($"Dependency {dependency.Name} already exists in this repository");
        }

        // Should the dependency go to Versions.props or global.json?
        if (_knownAssetNames.ContainsKey(dependency.Name))
        {
            if (!_sdkMapping.TryGetValue(dependency.Name, out var parent))
            {
                throw new Exception($"Dependency '{dependency.Name}' has no parent mapping defined.");
            }

            await AddDependencyToGlobalJson(repoUri, branch, parent, dependency, repoIsVmr);
        }
        else
        {
            await AddDependencyToVersionsPropsAsync(repoUri, branch, dependency);
        }

        await AddDependencyToVersionDetailsAsync(repoUri, branch, dependency);
    }

    public async Task RemoveDependencyAsync(DependencyDetail dependency, string repoUri, string branch, bool repoIsVmr = false)
    {
        var updatedDependencyVersionFile =
            new GitFile(VersionFiles.VersionDetailsXml, await RemoveDependencyFromVersionDetailsAsync(dependency, repoUri, branch));
        var updatedVersionPropsFile =
            new GitFile(VersionFiles.VersionProps, await RemoveDependencyFromVersionPropsAsync(dependency, repoUri, branch));
        List<GitFile> gitFiles = [updatedDependencyVersionFile, updatedVersionPropsFile];

        var updatedDotnetTools = await RemoveDotnetToolsDependencyAsync(dependency, repoUri, branch, repoIsVmr);
        if (updatedDotnetTools != null)
        {
            gitFiles.Add(new(VersionFiles.DotnetToolsConfigJson, updatedDotnetTools));  
        }

        await GetGitClient(repoUri).CommitFilesAsync(
            gitFiles,
            repoUri,
            branch,
            $"Remove {dependency.Name} from Version.Details.xml and Version.props'");

        _logger.LogInformation($"Dependency '{dependency.Name}' successfully removed from '{VersionFiles.VersionDetailsXml}'");
    }

    private async Task<JObject> RemoveDotnetToolsDependencyAsync(DependencyDetail dependency, string repoUri, string branch, bool repoIsVmr)
    {
        var dotnetTools = await ReadDotNetToolsConfigJsonAsync(repoUri, branch, repoIsVmr);
        if (dotnetTools != null)
        {
            var tools = dotnetTools["tools"] as JObject;
            if (tools != null)
            {
                // we have to do this because JObject is case sensitive
                var toolProperty = tools.Properties().FirstOrDefault(p => p.Name.Equals(dependency.Name, StringComparison.OrdinalIgnoreCase));
                if (toolProperty != null)
                {
                    tools.Remove(toolProperty.Name);
                }

                return dotnetTools;
            }
        }

        return null;
    }

    private async Task<XmlDocument> RemoveDependencyFromVersionPropsAsync(DependencyDetail dependency, string repoUri, string branch)
    {
        var versionProps = await ReadVersionPropsAsync(repoUri, branch);
        string nodeName = VersionFiles.GetVersionPropsPackageVersionElementName(dependency.Name);
        XmlNode element = versionProps.SelectSingleNode($"//{nodeName}");
        if (element == null)
        {
            string alternateNodeName = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(dependency.Name);
            element = versionProps.SelectSingleNode($"//{alternateNodeName}");
            if (element == null)
            {
                throw new DependencyException($"Couldn't find dependency {dependency.Name} in Version.props");
            }
        }
        element.ParentNode.RemoveChild(element);

        return versionProps;
    }

    private async Task<XmlDocument> RemoveDependencyFromVersionDetailsAsync(DependencyDetail dependency, string repoUri, string branch)
    {
        var versionDetails = await ReadVersionDetailsXmlAsync(repoUri, branch);
        XmlNode dependencyNode = versionDetails.SelectSingleNode($"//{VersionDetailsParser.DependencyElementName}[@Name='{dependency.Name}']");

        if (dependencyNode == null)
        {
            throw new DependencyException($"Dependency {dependency.Name} not found in Version.Details.xml");
        }

        dependencyNode.ParentNode.RemoveChild(dependencyNode);
        return versionDetails;
    }

    private static void SetAttribute(XmlDocument document, XmlNode node, string name, string value)
    {
        XmlAttribute attribute = node.Attributes[name];
        if (attribute == null)
        {
            node.Attributes.Append(attribute = document.CreateAttribute(name));
        }
        attribute.Value = value;
    }

    private static XmlNode SetElement(XmlDocument document, XmlNode node, string name, string value = null, bool replace = true)
    {
        XmlNode element = node.SelectSingleNode(name);
        if (element == null || !replace)
        {
            element = node.AppendChild(document.CreateElement(name));
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            element.InnerText = value;
        }
        return element;
    }

    public void UpdateVersionDetails(
        XmlDocument versionDetails,
        IEnumerable<DependencyDetail> itemsToUpdate,
        SourceDependency sourceDependency,
        IEnumerable<DependencyDetail> oldDependencies)
    {
        // Adds/updates the <Source> element
        if (sourceDependency != null)
        {
            var sourceNode = versionDetails.SelectSingleNode($"//{VersionDetailsParser.SourceElementName}");
            if (sourceNode == null)
            {
                sourceNode = versionDetails.CreateElement(VersionDetailsParser.SourceElementName);
                var dependenciesNode = versionDetails.SelectSingleNode($"//{VersionDetailsParser.DependenciesElementName}");
                dependenciesNode.PrependChild(sourceNode);
            }

            SetAttribute(versionDetails, sourceNode, VersionDetailsParser.UriElementName, sourceDependency.Uri);
            SetAttribute(versionDetails, sourceNode, VersionDetailsParser.ShaElementName, sourceDependency.Sha);
            if (sourceDependency.BarId != null) {
                SetAttribute(versionDetails, sourceNode, VersionDetailsParser.BarIdElementName, sourceDependency.BarId.ToString());
            }
        }

        foreach (DependencyDetail itemToUpdate in itemsToUpdate)
        {
            itemToUpdate.Validate();

            // Double check that the dependency is not pinned
            if (itemToUpdate.Pinned)
            {
                throw new DarcException($"An attempt to update pinned dependency '{itemToUpdate.Name}' was made");
            }

            // Use a case-insensitive update.
            XmlNodeList versionList = versionDetails.SelectNodes($"//{VersionDetailsParser.DependencyElementName}[translate(@Name,'ABCDEFGHIJKLMNOPQRSTUVWXYZ'," +
                                                                 $"'abcdefghijklmnopqrstuvwxyz')='{itemToUpdate.Name.ToLower()}']");

            if (versionList.Count != 1)
            {
                if (versionList.Count == 0)
                {
                    throw new DependencyException($"No dependencies named '{itemToUpdate.Name}' found.");
                }
                else
                {
                    throw new DarcException($"The use of the same asset '{itemToUpdate.Name}', even with a different version, is currently not " +
                                            "supported.");
                }
            }

            XmlNode nodeToUpdate = versionList.Item(0);

            SetAttribute(versionDetails, nodeToUpdate, VersionDetailsParser.VersionAttributeName, itemToUpdate.Version);
            SetAttribute(versionDetails, nodeToUpdate, VersionDetailsParser.NameAttributeName, itemToUpdate.Name);
            SetElement(versionDetails, nodeToUpdate, VersionDetailsParser.ShaElementName, itemToUpdate.Commit);
            SetElement(versionDetails, nodeToUpdate, VersionDetailsParser.UriElementName, itemToUpdate.RepoUri);
        }
    }

    public async Task<GitFileContentContainer> UpdateDependencyFiles(
        IEnumerable<DependencyDetail> itemsToUpdate,
        SourceDependency sourceDependency,
        string repoUri,
        string branch,
        IEnumerable<DependencyDetail> oldDependencies,
        SemanticVersion incomingDotNetSdkVersion)
    {
        // When updating version files, we always want to look in the base folder, even when we're updating it in the VMR
        // src/arcade version files only get updated during arcade forward flows
        bool repoIsVmr = false;
        XmlDocument versionDetails = await ReadVersionDetailsXmlAsync(repoUri, branch);
        XmlDocument versionProps = await ReadVersionPropsAsync(repoUri, branch);
        JObject globalJson = await ReadGlobalJsonAsync(repoUri, branch, repoIsVmr);
        JObject toolsConfigurationJson = await ReadDotNetToolsConfigJsonAsync(repoUri, branch, repoIsVmr);
        XmlDocument nugetConfig = await ReadNugetConfigAsync(repoUri, branch);

        foreach (DependencyDetail itemToUpdate in itemsToUpdate)
        {
            try
            {
                itemToUpdate.Validate();
            }
            catch (DarcException e)
            {
                throw new DarcException(e.Message + $" in repo '{repoUri}' and branch '{branch}'", e);
            }

            UpdateVersionFiles(versionProps, globalJson, toolsConfigurationJson, itemToUpdate);
        }

        UpdateVersionDetails(versionDetails, itemsToUpdate, sourceDependency, oldDependencies);

        // Combine the two sets of dependencies. If an asset is present in the itemsToUpdate,
        // prefer that one over the old dependencies
        Dictionary<string, HashSet<string>> itemsToUpdateLocations = GetAssetLocationMapping(itemsToUpdate);

        if (oldDependencies != null)
        {
            foreach (DependencyDetail dependency in oldDependencies)
            {
                if (!itemsToUpdateLocations.ContainsKey(dependency.Name) && dependency.Locations != null)
                {
                    itemsToUpdateLocations.Add(dependency.Name, [.. dependency.Locations]);
                }
            }
        }

        // At this point we only care about the Maestro managed locations for the assets.
        // Flatten the dictionary into a set that has all the managed feeds
        Dictionary<string, HashSet<string>> managedFeeds = FlattenLocationsAndSplitIntoGroups(itemsToUpdateLocations);
        var updatedNugetConfig = UpdatePackageSources(nugetConfig, managedFeeds);

        // Update the dotnet sdk if necessary
        Dictionary<GitFileMetadataName, string> globalJsonMetadata = null;
        if (incomingDotNetSdkVersion != null)
        {
            globalJsonMetadata = UpdateDotnetVersionGlobalJson(incomingDotNetSdkVersion, globalJson);
        }

        var fileContainer = new GitFileContentContainer
        {
            GlobalJson = new GitFile(VersionFiles.GlobalJson, globalJson, globalJsonMetadata),
            VersionDetailsXml = new GitFile(VersionFiles.VersionDetailsXml, versionDetails),
            VersionProps = new GitFile(VersionFiles.VersionProps, versionProps),
            NugetConfig = new GitFile(VersionFiles.NugetConfig, updatedNugetConfig)
        };

        // dotnet-tools.json is optional, so only include it if it was found.
        if (toolsConfigurationJson != null)
        {
            fileContainer.DotNetToolsJson = new GitFile(VersionFiles.DotnetToolsConfigJson, toolsConfigurationJson);
        }

        return fileContainer;
    }

    /// <summary>
    /// Updates the global.json entries for tools.dotnet and sdk.version if they are older than an incoming version
    /// </summary>
    /// <param name="incomingDotnetVersion">version to compare against</param>
    /// <param name="repoGlobalJson">Global.Json file to update</param>
    /// <returns>Updated global.json file if was able to update, or the unchanged global.json if unable to</returns>
    private Dictionary<GitFileMetadataName, string> UpdateDotnetVersionGlobalJson(SemanticVersion incomingDotnetVersion, JObject globalJson)
    {
        try
        {
            if (SemanticVersion.TryParse(globalJson.SelectToken("tools.dotnet")?.ToString(), out SemanticVersion repoDotnetVersion))
            {
                if (repoDotnetVersion.CompareTo(incomingDotnetVersion) < 0)
                {
                    Dictionary<GitFileMetadataName, string> metadata = [];

                    globalJson["tools"]["dotnet"] = incomingDotnetVersion.ToNormalizedString();
                    metadata.Add(GitFileMetadataName.ToolsDotNetUpdate, incomingDotnetVersion.ToNormalizedString());

                    // Also update and keep sdk.version in sync.
                    JToken sdkVersion = globalJson.SelectToken("sdk.version");
                    if (sdkVersion != null)
                    {
                        globalJson["sdk"]["version"] = incomingDotnetVersion.ToNormalizedString();
                        metadata.Add(GitFileMetadataName.SdkVersionUpdate, incomingDotnetVersion.ToNormalizedString());
                    }

                    return metadata;
                }
            }
            else
            {
                _logger.LogError("Could not parse the repo's dotnet version from the global.json. Skipping update to dotnet version sections");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Dotnet version for global.json. Skipping update to version sections.");
        }

        // No updates
        return null;
    }

    private static bool IsOnlyPresentInMaestroManagedFeed(HashSet<string> locations)
    {
        return locations != null && locations.All(IsMaestroManagedFeed);
    }

    private static bool IsMaestroManagedFeed(string feed)
    {
        return FeedConstants.MaestroManagedFeedPatterns.Any(p => Regex.IsMatch(feed, p)) ||
               Regex.IsMatch(feed, FeedConstants.AzureStorageProxyFeedPattern);
    }

    public XmlDocument UpdatePackageSources(XmlDocument nugetConfig, Dictionary<string, HashSet<string>> maestroManagedFeedsByRepo)
    {
        // Reconstruct the PackageSources section with the feeds
        XmlNode packageSourcesNode = nugetConfig.SelectSingleNode("//configuration/packageSources");
        if (packageSourcesNode == null)
        {
            _logger.LogError("Did not find a <packageSources> element in NuGet.config");
            return nugetConfig;
        }

        const string addPackageSourcesElementName = "add";

        XmlNode currentNode = packageSourcesNode.FirstChild;

        // This will be used to denote whether we should delete a managed source. Managed sources should only
        // be deleted within the maestro comment block. This allows for repository owners to use specific feeds from
        // other channels or releases in special cases.
        var withinMaestroComments = false;

        // Remove all managed feeds and Maestro's comments
        while (currentNode != null)
        {
            if (currentNode.NodeType == XmlNodeType.Element)
            {
                if (currentNode.Name.Equals(addPackageSourcesElementName, StringComparison.OrdinalIgnoreCase))
                {
                    // Get the feed value
                    var feedValue = currentNode.Attributes["value"];
                    if (feedValue == null)
                    {
                        // This is unexpected, error
                        _logger.LogError("NuGet.config 'add' element did not have a feed 'value' attribute.");
                        return nugetConfig;
                    }

                    if (withinMaestroComments && IsMaestroManagedFeed(feedValue.Value))
                    {
                        currentNode = RemoveCurrentNode(currentNode);
                        continue;
                    }
                }
                // Remove the clear element wherever it is.
                // It will be added when we add the maestro managed sources.
                else if (currentNode.Name.Equals(VersionDetailsParser.ClearElement, StringComparison.OrdinalIgnoreCase))
                {
                    currentNode = RemoveCurrentNode(currentNode);
                    continue;
                }
            }
            else if (currentNode.NodeType == XmlNodeType.Comment)
            {
                if (currentNode.Value.Equals(MaestroBeginComment, StringComparison.OrdinalIgnoreCase))
                {
                    withinMaestroComments = true;
                }
                else if (currentNode.Value.Equals(MaestroEndComment, StringComparison.OrdinalIgnoreCase))
                {
                    withinMaestroComments = false;
                }
            }

            currentNode = currentNode.NextSibling;
        }

        InsertManagedPackagesBlock(nugetConfig, packageSourcesNode, maestroManagedFeedsByRepo);

        CreateOrUpdateDisabledSourcesBlock(nugetConfig, maestroManagedFeedsByRepo, FeedConstants.MaestroManagedInternalFeedPrefix);

        return nugetConfig;
    }

    /// <summary>
    /// Remove the current node and return the next node that should be walked
    /// </summary>
    /// <param name="toRemove">Node to remove</param>
    /// <returns>Next node to walk</returns>
    private static XmlNode RemoveCurrentNode(XmlNode toRemove)
    {
        var nextNodeToWalk = toRemove.NextSibling;
        toRemove.ParentNode.RemoveChild(toRemove);
        return nextNodeToWalk;
    }

    // Ensure that the file contains a <disabledPackageSources> node.
    // - If it exists, do not modify values other than key nodes starting with disableFeedKeyPrefix,
    //   which we'll ensure are after any <clear/> tags and set to 'true'
    // - If it does not exist, add it
    // - Ensure all disableFeedKeyPrefix key values have entries under <disabledPackageSources> with value="true"
    private void CreateOrUpdateDisabledSourcesBlock(XmlDocument nugetConfig, Dictionary<string, HashSet<string>> maestroManagedFeedsByRepo, string disableFeedKeyPrefix)
    {
        _logger.LogInformation($"Ensuring a <disabledPackageSources> node exists and is actively disabling any feed starting with {disableFeedKeyPrefix}");
        XmlNode disabledSourcesNode = nugetConfig.SelectSingleNode("//configuration/disabledPackageSources");
        XmlNode insertAfterNode = null;

        if (disabledSourcesNode == null)
        {
            XmlNode configNode = nugetConfig.SelectSingleNode("//configuration");
            _logger.LogInformation("Config file did not previously have <disabledSourcesNode>, adding");
            disabledSourcesNode = nugetConfig.CreateElement("disabledPackageSources");
            configNode.AppendChild(disabledSourcesNode);
        }
        // If there's a clear node in the children of the disabledSources, we want to put any of our entries after the last one seen.
        else if (disabledSourcesNode.HasChildNodes)
        {
            var withinMaestroComments = false;
            var allPossibleManagedSources = new List<string>();

            foreach (var repoName in maestroManagedFeedsByRepo.Keys)
            {
                allPossibleManagedSources.AddRange(GetManagedPackageSources(maestroManagedFeedsByRepo[repoName]).Select(ms => ms.key).ToList());
            }

            XmlNode currentNode = disabledSourcesNode.FirstChild;

            while (currentNode != null)
            {
                if (currentNode.Name.Equals("clear", StringComparison.InvariantCultureIgnoreCase))
                {
                    insertAfterNode = currentNode;
                }
                // while we traverse, remove all existing entries for what we're updating if inside the comment block
                else if (currentNode.Name.Equals("add", StringComparison.InvariantCultureIgnoreCase) &&
                         currentNode.Attributes["key"]?.Value.StartsWith(disableFeedKeyPrefix) == true &&
                         withinMaestroComments)
                {
                    currentNode = RemoveCurrentNode(currentNode);
                    continue;
                }
                else if (currentNode.NodeType == XmlNodeType.Comment)
                {
                    if (currentNode.Value.Equals(MaestroBeginComment, StringComparison.OrdinalIgnoreCase))
                    {
                        withinMaestroComments = true;
                    }
                    else if (currentNode.Value.Equals(MaestroEndComment, StringComparison.OrdinalIgnoreCase))
                    {
                        withinMaestroComments = false;
                    }
                }
                currentNode = currentNode.NextSibling;
            }

            if (insertAfterNode != null)
            {
                _logger.LogInformation("Found a <clear/> in disabledPackageSources; will insert or update as needed after it.");
            }
        }

        XmlComment startCommentBlock = GetFirstMatchingComment(disabledSourcesNode, MaestroBeginComment);
        if (startCommentBlock != null)
        {
            insertAfterNode = startCommentBlock;
        }

        XmlComment endCommentBlock = GetFirstMatchingComment(disabledSourcesNode, MaestroEndComment);
        var introducedAStartCommentBlock = false;

        foreach (var repoName in maestroManagedFeedsByRepo.Keys.OrderBy(t => t))
        {
            var managedSources = GetManagedPackageSources(maestroManagedFeedsByRepo[repoName]).OrderBy(t => t.feed).ToList();

            // If this set of sources doesn't have one, just keep going
            if (!managedSources.Any(m => m.key.StartsWith(disableFeedKeyPrefix, StringComparison.InvariantCultureIgnoreCase)))
            {
                continue;
            }

            // For a config that doesn't already have the outermost 'begin' comment, create it.
            if (startCommentBlock == null)
            {
                if (insertAfterNode != null)
                {
                    insertAfterNode = disabledSourcesNode.InsertAfter(nugetConfig.CreateComment(MaestroBeginComment), insertAfterNode);
                }
                else
                {
                    insertAfterNode = disabledSourcesNode.InsertAfter(nugetConfig.CreateComment(MaestroBeginComment), disabledSourcesNode.FirstChild);
                }
                startCommentBlock = (XmlComment)insertAfterNode;
                introducedAStartCommentBlock = true;
            }

            // We'll insert after the begin comment
            XmlComment startDisabled = GetFirstMatchingComment(disabledSourcesNode, $"{MaestroRepoSpecificBeginComment} {repoName} ");
            if (startDisabled != null)
            {
                insertAfterNode = startDisabled;
            }
            else
            {
                startDisabled = nugetConfig.CreateComment($"{MaestroRepoSpecificBeginComment} {repoName} ");
                insertAfterNode = disabledSourcesNode.InsertAfter(startDisabled, insertAfterNode);
            }

            foreach (var (key, _) in managedSources.Where(m => m.key.StartsWith(disableFeedKeyPrefix, StringComparison.InvariantCultureIgnoreCase)))
            {
                XmlElement addEntry = nugetConfig.CreateElement("add");
                addEntry.SetAttribute("key", key);
                addEntry.SetAttribute("value", "true");
                insertAfterNode = disabledSourcesNode.InsertAfter(addEntry, insertAfterNode);
            }

            // We'll insert after the begin comment
            XmlComment endDisabled = GetFirstMatchingComment(disabledSourcesNode, $"{MaestroRepoSpecificEndComment} {repoName} ");
            if (endDisabled != null)
            {
                insertAfterNode = startDisabled;
            }
            else
            {
                endDisabled = nugetConfig.CreateComment($"{MaestroRepoSpecificEndComment} {repoName} ");
                insertAfterNode = disabledSourcesNode.InsertAfter(endDisabled, insertAfterNode);
            }
        }

        // For a config that doesn't already have the end comment, create it.
        if (endCommentBlock == null && introducedAStartCommentBlock)
        {
            endCommentBlock = (XmlComment)disabledSourcesNode.InsertAfter(nugetConfig.CreateComment(MaestroEndComment), insertAfterNode);
        }
    }

    // Insert the following structure at the beginning of the nodes pointed by `packageSourcesNode`.
    // <clear/>
    // <MaestroBeginComment />
    // managedSources*
    // <MaestroEndComment />
    private void InsertManagedPackagesBlock(XmlDocument nugetConfig, XmlNode packageSourcesNode, Dictionary<string, HashSet<string>> maestroManagedFeedsByRepo)
    {
        var clearNode = nugetConfig.CreateElement(VersionDetailsParser.ClearElement);
        XmlNode currentNode = packageSourcesNode.PrependChild(clearNode);

        if (maestroManagedFeedsByRepo.Values.Count == 0)
        {
            return;
        }

        var repoList = maestroManagedFeedsByRepo.Keys.OrderBy(t => t).ToList();

        XmlComment blockBeginComment = GetFirstMatchingComment(packageSourcesNode, MaestroBeginComment);
        blockBeginComment ??= (XmlComment)packageSourcesNode.InsertAfter(nugetConfig.CreateComment(MaestroBeginComment), clearNode);

        currentNode = blockBeginComment;

        foreach (var repository in repoList)
        {
            var managedSources = GetManagedPackageSources(maestroManagedFeedsByRepo[repository]).OrderByDescending(t => t.feed).ToList();

            var startBlockComment = GetFirstMatchingComment(packageSourcesNode, $"{MaestroRepoSpecificBeginComment} {repository} ");
            if (startBlockComment == null)
            {
                startBlockComment = nugetConfig.CreateComment($"{MaestroRepoSpecificBeginComment} {repository} ");
                currentNode = packageSourcesNode.InsertAfter(startBlockComment, currentNode);
            }
            else
            {
                currentNode = startBlockComment;
            }

            foreach ((var key, var feed) in managedSources)
            {
                var newElement = nugetConfig.CreateElement(VersionDetailsParser.AddElement);

                SetAttribute(nugetConfig, newElement, VersionDetailsParser.KeyAttributeName, key);
                SetAttribute(nugetConfig, newElement, VersionDetailsParser.ValueAttributeName, feed);

                currentNode = packageSourcesNode.InsertAfter(newElement, currentNode);
            }

            var endBlockComment = GetFirstMatchingComment(packageSourcesNode, $"{MaestroRepoSpecificEndComment} {repository} ");
            if (endBlockComment == null)
            {
                endBlockComment = nugetConfig.CreateComment($"{MaestroRepoSpecificEndComment} {repository} ");
                currentNode = packageSourcesNode.InsertAfter(endBlockComment, currentNode);
            }
            else
            {
                currentNode = endBlockComment;
            }
        }

        if (GetFirstMatchingComment(packageSourcesNode, MaestroEndComment) == null)
        {
            packageSourcesNode.InsertAfter(nugetConfig.CreateComment(MaestroEndComment), currentNode);
        }
    }

    private static XmlComment GetFirstMatchingComment(XmlNode nodeToCheck, string commentText)
    {
        if (nodeToCheck.HasChildNodes)
        {
            XmlNode currentNode = nodeToCheck.FirstChild;

            while (currentNode != null)
            {
                if (currentNode.NodeType == XmlNodeType.Comment &&
                    currentNode.Value.Equals(commentText, StringComparison.OrdinalIgnoreCase))
                {
                    return (XmlComment)currentNode;
                }
                currentNode = currentNode.NextSibling;
            }
        }
        return null;
    }

    private async Task AddDependencyToVersionDetailsAsync(
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
        await GetGitClient(repo).CommitFilesAsync([file], repo, branch, $"Add {dependency} to " +
            $"'{VersionFiles.VersionDetailsXml}'");

        _logger.LogInformation(
            $"Dependency '{dependency.Name}' with version '{dependency.Version}' successfully added to " +
            $"'{VersionFiles.VersionDetailsXml}'");
    }

    /// <summary>
    ///     <!-- Package versions -->
    ///     <PropertyGroup>
    ///         <MicrosoftDotNetApiCompatPackageVersion>1.0.0-beta.18478.5</MicrosoftDotNetApiCompatPackageVersion>
    ///     </PropertyGroup>
    ///
    ///     See https://github.com/dotnet/arcade/blob/main/Documentation/DependencyDescriptionFormat.md for more
    ///     information.
    /// </summary>
    /// <param name="repo">Path to Versions.props file</param>
    /// <param name="dependency">Dependency information to add.</param>
    private async Task AddDependencyToVersionsPropsAsync(string repo, string branch, DependencyDetail dependency)
    {
        XmlDocument versionProps = await ReadVersionPropsAsync(repo, null);
        var documentNamespaceUri = versionProps.DocumentElement.NamespaceURI;

        var packageVersionElementName = VersionFiles.GetVersionPropsPackageVersionElementName(dependency.Name);
        var packageVersionAlternateElementName = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(
            dependency.Name);

        // Attempt to find the element name or alternate element name under
        // the property group nodes
        XmlNode existingVersionNode = versionProps.DocumentElement.SelectSingleNode($"//*[local-name()='{packageVersionElementName}' and parent::PropertyGroup]")
            ?? versionProps.DocumentElement.SelectSingleNode($"//*[local-name()='{packageVersionAlternateElementName}' and parent::PropertyGroup]");

        if (existingVersionNode != null)
        {
            existingVersionNode.InnerText = dependency.Version;
        }
        else
        {
            // Select elements by local name, since the Project (DocumentElement) element often has a default
            // xmlns set.
            XmlNodeList propertyGroupNodes = versionProps.DocumentElement.SelectNodes($"//*[local-name()='PropertyGroup']");

            var addedPackageVersionElement = false;
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
        await GetGitClient(repo).CommitFilesAsync([file], repo, branch, $"Add {dependency} to " +
            $"'{VersionFiles.VersionProps}'");

        _logger.LogInformation(
            $"Dependency '{dependency.Name}' with version '{dependency.Version}' successfully added to " +
            $"'{VersionFiles.VersionProps}'");
    }

    private async Task AddDependencyToGlobalJson(
        string repoUri,
        string branch,
        string parentField,
        DependencyDetail dependency,
        bool repoIsVmr = false)
    {
        JToken versionProperty = new JProperty(dependency.Name, dependency.Version);
        JObject globalJson = await ReadGlobalJsonAsync(repoUri, branch, repoIsVmr);
        JToken parent = globalJson[parentField];

        if (parent != null)
        {
            parent.Last.AddAfterSelf(versionProperty);
        }
        else
        {
            globalJson.Add(new JProperty(parentField, new JObject(versionProperty)));
        }

        var globalJsonPath = repoIsVmr ? VmrInfo.ArcadeRepoDir / VersionFiles.GlobalJson: VersionFiles.GlobalJson;
        var file = new GitFile(globalJsonPath, globalJson);
        await GetGitClient(repoUri).CommitFilesAsync(
            [file],
            repoUri,
            branch,
            $"Add {dependency.Name} to '{VersionFiles.GlobalJson}'");

        _logger.LogInformation($"Dependency '{dependency.Name}' with version '{dependency.Version}' successfully added to global.json");
    }

    public static XmlDocument GetXmlDocument(string fileContent)
    {
        var document = new XmlDocument
        {
            PreserveWhitespace = true
        };
        document.LoadXml(fileContent);

        return document;
    }

    private async Task<XmlDocument> ReadXmlFileAsync(string filePath, string repoUri, string branch)
    {
        _logger.LogInformation($"Reading '{filePath}' in repo '{repoUri}' and branch '{branch}'...");

        var fileContent = await GetGitClient(repoUri).GetFileContentsAsync(filePath, repoUri, branch);

        try
        {
            XmlDocument document = GetXmlDocument(fileContent);

            _logger.LogInformation($"Reading '{filePath}' from repo '{repoUri}' and branch '{branch}' succeeded!");

            return document;
        }
        catch (Exception exc)
        {
            _logger.LogError(exc, $"There was an exception while loading '{filePath}'");
            throw;
        }
    }

    /// <summary>
    ///     Update well-known version files.
    /// </summary>
    /// <param name="versionProps">Versions.props xml document</param>
    /// <param name="globalJsonToken">Global.json document</param>
    /// <param name="dotNetToolJsonToken">.config/dotnet-tools.json document</param>
    /// <param name="itemToUpdate">Item that needs an update.</param>
    /// <remarks>
    ///     TODO: https://github.com/dotnet/arcade/issues/1095
    /// </remarks>
    private void UpdateVersionFiles(XmlDocument versionProps, JToken globalJsonToken, JToken dotNetToolJsonToken, DependencyDetail itemToUpdate)
    {
        var versionElementName = VersionFiles.GetVersionPropsPackageVersionElementName(itemToUpdate.Name);
        var alternateVersionElementName = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(itemToUpdate.Name);

        // Select nodes case insensitively, then update the name.
        XmlNode packageVersionNode = versionProps.DocumentElement.SelectSingleNode(
            $"//*[translate(local-name(),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')=" +
            $"'{versionElementName.ToLower()}']");
        var foundElementName = versionElementName;

        // Find alternate names
        if (packageVersionNode == null)
        {
            packageVersionNode = versionProps.DocumentElement.SelectSingleNode(
                $"//*[translate(local-name(),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')=" +
                $"'{alternateVersionElementName.ToLower()}']");
            foundElementName = alternateVersionElementName;
        }

        if (packageVersionNode != null)
        {
            packageVersionNode.InnerText = itemToUpdate.Version;
            // If the node name was updated, then create a new node with the new name, unlink this node
            // and create a new one in the same location.
            if (packageVersionNode.LocalName != foundElementName)
            {
                {
                    XmlNode parentNode = packageVersionNode.ParentNode;
                    XmlNode newPackageVersionElement = versionProps.CreateElement(
                        foundElementName,
                        versionProps.DocumentElement.NamespaceURI);
                    newPackageVersionElement.InnerText = itemToUpdate.Version;
                    parentNode.ReplaceChild(newPackageVersionElement, packageVersionNode);
                }
            }
        }

        // Update the global json too, even if there was an element in the props file, in case
        // it was listed in both
        UpdateVersionGlobalJson(itemToUpdate, globalJsonToken);

        // If there is a .config/dotnet-tools.json file and this dependency exists there, update it too
        if (dotNetToolJsonToken != null)
        {
            UpdateDotNetToolsManifest(itemToUpdate, dotNetToolJsonToken);
        }
    }

    private static void UpdateVersionGlobalJson(DependencyDetail itemToUpdate, JToken token)
    {
        var versionElementName = VersionFiles.CalculateGlobalJsonElementName(itemToUpdate.Name);

        foreach (JProperty property in token.Children<JProperty>())
        {
            if (property.Name.Equals(versionElementName, StringComparison.OrdinalIgnoreCase))
            {
                property.Value = new JValue(itemToUpdate.Version);
                break;
            }

            UpdateVersionGlobalJson(itemToUpdate, property.Value);
        }
    }

    private void UpdateDotNetToolsManifest(DependencyDetail itemToUpdate, JToken token)
    {
        var versionElementName = itemToUpdate.Name;

        var toolsNode = (JObject)token["tools"];

        foreach (JProperty property in toolsNode?.Children<JProperty>())
        {
            if (property.Name.Equals(versionElementName, StringComparison.OrdinalIgnoreCase))
            {
                var versionEntry = (JValue)property.Value.SelectToken("version", false);

                if (versionEntry != null)
                {
                    versionEntry.Value = itemToUpdate.Version;
                }
                else
                {
                    _logger.LogError($"Entry found, but no version property to update, for dependency '{itemToUpdate.Name}'");
                }
                return;
            }
        }
    }

    /// <summary>
    ///     Verify the local repository has correct and consistent dependency information.
    ///     Currently, this implementation checks:
    ///     - global.json, Version.props and Version.Details.xml can be parsed.
    ///     - There are no duplicated dependencies in Version.Details.xml
    ///     - If a dependency exists in Version.Details.xml and in version.props/global.json, the versions match.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="branch"></param>
    /// <returns>Async task</returns>
    public async Task<bool> Verify(string repo, string branch)
    {
        Task<VersionDetails> versionDetails;
        Task<XmlDocument> versionProps;
        Task<JObject> globalJson;
        Task<JObject> dotnetToolsJson;
        // This operation doesn't support VMR verification
        bool repoIsVmr = false;

        try
        {
            versionDetails = ParseVersionDetailsXmlAsync(repo, branch);
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to parse {VersionFiles.VersionDetailsXml}");
            return false;
        }

        try
        {
            versionProps = ReadVersionPropsAsync(repo, branch);
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to read {VersionFiles.VersionProps}");
            return false;
        }

        try
        {
            globalJson = ReadGlobalJsonAsync(repo, branch, repoIsVmr);
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to read {VersionFiles.GlobalJson}");
            return false;
        }

        try
        {
            dotnetToolsJson = ReadDotNetToolsConfigJsonAsync(repo, branch, repoIsVmr);
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Failed to read {VersionFiles.DotnetToolsConfigJson}");
            return false;
        }

        List<Task<bool>> verificationTasks =
        [
            VerifyNoDuplicatedProperties(await versionProps),
            VerifyNoDuplicatedDependencies((await versionDetails).Dependencies),
            VerifyMatchingVersionProps(
                (await versionDetails).Dependencies,
                await versionProps,
                out Task<HashSet<string>> utilizedVersionPropsDependencies),
            VerifyMatchingGlobalJson(
                (await versionDetails).Dependencies,
                await globalJson,
                out Task<HashSet<string>> utilizedGlobalJsonDependencies),
            VerifyUtilizedDependencies(
                (await versionDetails).Dependencies,
                new List<HashSet<string>>
                {
                    await utilizedVersionPropsDependencies,
                    await utilizedGlobalJsonDependencies
                }),
            VerifyMatchingDotNetToolsJson(
                (await versionDetails).Dependencies,
                await dotnetToolsJson)
        ];

        var results = await Task.WhenAll(verificationTasks);
        return results.All(result => result);
    }

    public static void NormalizeAttributes(string directoryPath)
    {
        var filePaths = Directory.GetFiles(directoryPath);
        var subdirectoryPaths = Directory.GetDirectories(directoryPath);

        foreach (var filePath in filePaths)
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        foreach (var subdirectoryPath in subdirectoryPaths)
        {
            NormalizeAttributes(subdirectoryPath);
        }

        File.SetAttributes(directoryPath, FileAttributes.Normal);
    }

    /// <summary>
    ///     Ensure that there is a unique propertyName + condition on the list.
    /// </summary>
    /// <param name="versionProps">Xml object representing MSBuild properties file.</param>
    /// <returns>True if there are no duplicated properties.</returns>
    public Task<bool> VerifyNoDuplicatedProperties(XmlDocument versionProps)
    {
        var hasNoDuplicatedProperties = true;
        HashSet<string> existingProperties = [];

        XmlNodeList propertyGroups = versionProps.GetElementsByTagName("PropertyGroup");
        foreach (XmlNode propertyGroup in propertyGroups)
        {
            foreach (var property in propertyGroup.ChildNodes)
            {
                if (property is XmlElement)
                {
                    var element = property as XmlElement;
                    var propertyName = element.Name;

                    propertyName = Regex.Replace(propertyName, @"PackageVersion$", string.Empty);
                    propertyName = Regex.Replace(propertyName, @"Version$", string.Empty);

                    propertyName += element.GetAttribute("Condition");
                    propertyName += element.GetAttribute("condition");

                    if (existingProperties.Contains(propertyName))
                    {
                        _logger.LogError($"The dependency '{propertyName}' appears more than once in " +
                                         $"'{VersionFiles.VersionProps}'");
                        hasNoDuplicatedProperties = false;
                    }

                    existingProperties.Add(propertyName);
                }
            }
        }

        return Task.FromResult(hasNoDuplicatedProperties);
    }

    /// <summary>
    ///     Ensure that the dependency structure only contains one of each named dependency.
    /// </summary>
    /// <param name="dependencies">Parsed dependencies in the repository.</param>
    /// <returns>True if there are no duplicated dependencies.</returns>
    private Task<bool> VerifyNoDuplicatedDependencies(IEnumerable<DependencyDetail> dependencies)
    {
        var result = true;
        HashSet<string> dependenciesBitVector = [];
        foreach (var dependency in dependencies)
        {
            if (dependenciesBitVector.Contains(dependency.Name, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogError($"The dependency '{dependency.Name}' appears more than once in " +
                                 $"'{VersionFiles.VersionDetailsXml}'");
                result = false;
            }
            dependenciesBitVector.Add(dependency.Name);
        }
        return Task.FromResult(result);
    }

    /// <summary>
    ///     Verify that any dependency that exists in both Version.props and Version.Details.xml has matching version numbers.
    /// </summary>
    /// <param name="dependencies">Parsed dependencies in the repository.</param>
    /// <param name="versionProps">Parsed version props file</param>
    /// <returns></returns>
    private Task<bool> VerifyMatchingVersionProps(IEnumerable<DependencyDetail> dependencies, XmlDocument versionProps, out Task<HashSet<string>> utilizedDependencies)
    {
        HashSet<string> utilizedSet = [];
        var result = true;
        foreach (var dependency in dependencies)
        {
            var versionElementName = VersionFiles.GetVersionPropsPackageVersionElementName(dependency.Name);
            var alternateVersionElementName = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(dependency.Name);
            XmlNode versionNode = versionProps.DocumentElement.SelectSingleNode($"//*[local-name()='{versionElementName}']");
            if (versionNode == null)
            {
                versionNode = versionProps.DocumentElement.SelectSingleNode($"//*[local-name()='{alternateVersionElementName}']");
                versionElementName = alternateVersionElementName;
            }

            if (versionNode != null)
            {
                // Validate that the casing matches for consistency
                if (versionNode.Name != versionElementName)
                {
                    _logger.LogError($"The dependency '{dependency.Name}' has a case mismatch between " +
                                     $"'{VersionFiles.VersionProps}' and '{VersionFiles.VersionDetailsXml}' " +
                                     $"('{versionNode.Name}' vs. '{versionElementName}')");
                    result = false;
                }
                // Validate innner version matches
                if (versionNode.InnerText != dependency.Version)
                {
                    _logger.LogError($"The dependency '{dependency.Name}' has a version mismatch between " +
                                     $"'{VersionFiles.VersionProps}' and '{VersionFiles.VersionDetailsXml}' " +
                                     $"('{versionNode.InnerText}' vs. '{dependency.Version}')");
                    result = false;
                }
                utilizedSet.Add(dependency.Name);
            }
        }
        utilizedDependencies = Task.FromResult(utilizedSet);
        return Task.FromResult(result);
    }

    /// <summary>
    ///     Verify that any dependency that exists in global.json and Version.Details.xml (e.g. Arcade SDK)
    ///     has matching version numbers.
    /// </summary>
    /// <param name="dependencies">Parsed dependencies in the repository.</param>
    /// <param name="rootToken">Root global.json token.</param>
    /// <returns></returns>
    private Task<bool> VerifyMatchingGlobalJson(
        IEnumerable<DependencyDetail> dependencies,
        JObject rootToken,
        out Task<HashSet<string>> utilizedDependencies)
    {
        HashSet<string> utilizedSet = [];
        var result = true;
        foreach (var dependency in dependencies)
        {
            var versionedName = VersionFiles.CalculateGlobalJsonElementName(dependency.Name);
            JToken dependencyNode = FindDependency(rootToken, versionedName);
            if (dependencyNode != null)
            {
                // Should be a string with matching version.
                if (dependencyNode.Type != JTokenType.Property || ((JProperty)dependencyNode).Value.Type != JTokenType.String)
                {
                    _logger.LogError($"The element '{dependency.Name}' in '{VersionFiles.GlobalJson}' should be a property " +
                                     $"with a value of type string.");
                    result = false;
                    continue;
                }
                var property = (JProperty)dependencyNode;
                // Validate that the casing matches for consistency
                if (property.Name != versionedName)
                {
                    _logger.LogError($"The dependency '{dependency.Name}' has a case mismatch between " +
                                     $"'{VersionFiles.GlobalJson}' and '{VersionFiles.VersionDetailsXml}' " +
                                     $"('{property.Name}' vs. '{versionedName}')");
                    result = false;
                }
                // Validate version
                var value = property.Value;
                if (value.Value<string>() != dependency.Version)
                {
                    _logger.LogError($"The dependency '{dependency.Name}' has a version mismatch between " +
                                     $"'{VersionFiles.GlobalJson}' and '{VersionFiles.VersionDetailsXml}' " +
                                     $"('{value.Value<string>()}' vs. '{dependency.Version}')");
                }

                utilizedSet.Add(dependency.Name);
            }
        }
        utilizedDependencies = Task.FromResult(utilizedSet);
        return Task.FromResult(result);
    }

    /// <summary>
    ///     Verify that any dependency details we're flowing have a matching version number
    ///     in the .config/dotnet-tools.json file (but only if it exists)
    /// </summary>
    /// <param name="dependencies">Parsed dependencies in the repository.</param>
    /// <param name="rootToken">Root global.json token.</param>
    /// <returns></returns>
    private Task<bool> VerifyMatchingDotNetToolsJson(
        IEnumerable<DependencyDetail> dependencies,
        JObject rootToken)
    {
        var result = true;
        // If there isn't a .config/dotnet-tools.json, skip checking
        if (rootToken != null)
        {
            foreach (var dependency in dependencies)
            {
                var versionedName = VersionFiles.CalculateDotnetToolsJsonElementName(dependency.Name);
                JToken dependencyNode = FindDependency(rootToken, versionedName);
                if (dependencyNode != null)
                {
                    var specifiedVersion = dependencyNode.Children().FirstOrDefault()?["version"];

                    if (specifiedVersion == null)
                    {
                        _logger.LogError($"The element 'version' in '{VersionFiles.DotnetToolsConfigJson}' was not found.'");
                        result = false;
                        continue;
                    }

                    var property = (JProperty)dependencyNode;
                    // Validate that the casing matches for consistency
                    if (property.Name != versionedName)
                    {
                        _logger.LogError($"The dependency '{dependency.Name}' has a case mismatch between " +
                                         $"'{VersionFiles.GlobalJson}' and '{VersionFiles.VersionDetailsXml}' " +
                                         $"('{property.Name}' vs. '{versionedName}')");
                        result = false;
                    }
                    // Validate version
                    if (specifiedVersion.Value<string>() != dependency.Version)
                    {
                        _logger.LogError($"The dependency '{dependency.Name}' has a version mismatch between " +
                                         $"'{VersionFiles.GlobalJson}' and '{VersionFiles.VersionDetailsXml}' " +
                                         $"('{specifiedVersion.Value<string>()}' vs. '{dependency.Version}')");
                    }
                }
            }
        }
        return Task.FromResult(result);
    }

    /// <summary>
    ///     Recursively walks a json tree to find a property called <paramref name="elementName"/> in its children
    /// </summary>
    /// <param name="currentToken">Current token to walk.</param>
    /// <param name="elementName">Property name to find.</param>
    /// <returns>Token with name 'name' or null if it does not exist.</returns>
    private static JToken FindDependency(JToken currentToken, string elementName)
    {
        foreach (JProperty property in currentToken.Children<JProperty>())
        {
            if (property.Name.Equals(elementName, StringComparison.OrdinalIgnoreCase))
            {
                return property;
            }

            JToken foundToken = FindDependency(property.Value, elementName);
            if (foundToken != null)
            {
                return foundToken;
            }
        }

        return null;
    }

    /// <summary>
    ///     Check that each dependency in <paramref name="dependencies"/> exists in at least one of the
    ///     <paramref name="utilizedDependencySets"/>
    /// </summary>
    /// <param name="dependencies">Parsed dependencies in the repository.</param>
    /// <param name="utilizedDependencySets">Bit vectors dependency expression locations.</param>
    /// <returns></returns>
    private Task<bool> VerifyUtilizedDependencies(
        IEnumerable<DependencyDetail> dependencies,
        IEnumerable<HashSet<string>> utilizedDependencySets)
    {
        var result = true;
        foreach (var dependency in dependencies)
        {
            if (!utilizedDependencySets.Where(set => set.Contains(dependency.Name)).Any())
            {
                _logger.LogWarning($"The dependency '{dependency.Name}' is unused in either '{VersionFiles.GlobalJson}' " +
                                   $"or '{VersionFiles.VersionProps}'");
                result = false;
            }
        }
        return Task.FromResult(result);
    }

    /// <summary>
    ///  Infer repo names from feeds using regex.
    ///  If any feed name resolution fails, we'll just put it into an "unknown" bucket.
    /// </summary>
    /// <param name="assetLocationMap">Dictionary of all feeds by their location</param>
    /// <returns>Dictionary with key = repo name for logging, value = hashset of feeds</returns>
    public Dictionary<string, HashSet<string>> FlattenLocationsAndSplitIntoGroups(Dictionary<string, HashSet<string>> assetLocationMap)
    {
        HashSet<string> allManagedFeeds = [];
        foreach (var asset in assetLocationMap.Keys)
        {
            if (IsOnlyPresentInMaestroManagedFeed(assetLocationMap[asset]))
            {
                allManagedFeeds.UnionWith(assetLocationMap[asset]);
            }
        }

        var unableToResolveName = "unknown";
        Dictionary<string, HashSet<string>> result = [];
        foreach (var feedUri in allManagedFeeds)
        {
            var repoNameFromFeed = string.Empty;
            try
            {
                var match = Regex.Match(feedUri, FeedConstants.MaestroManagedFeedNamePattern);
                // We only care about #3 (formatted repo name), but if the count isn't constant, something's wrong.
                if (match.Success && match.Groups.Count == 6)
                {
                    repoNameFromFeed = match.Groups[3].Value;
                }

                if (string.IsNullOrEmpty(repoNameFromFeed))
                {
                    repoNameFromFeed = unableToResolveName;
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to use regex to determine repo information from feed");
                repoNameFromFeed = unableToResolveName;
            }

            if (!result.ContainsKey(repoNameFromFeed))
            {
                result.Add(repoNameFromFeed, []);
            }
            result[repoNameFromFeed].Add(feedUri);
        }
        return result;
    }

    public List<(string key, string feed)> GetPackageSources(XmlDocument nugetConfig, Func<string, bool> filter = null)
    {
        var sources = new List<(string key, string feed)>();
        XmlNodeList nodes = nugetConfig.SelectNodes("//configuration/packageSources/add");
        foreach (XmlNode node in nodes)
        {
            if (node.NodeType == XmlNodeType.Element)
            {
                var keyContent = node.Attributes["key"]?.Value;
                var valueContent = node.Attributes["value"]?.Value;
                if (keyContent != null && valueContent != null)
                {
                    if (filter != null && !filter(valueContent))
                    {
                        continue;
                    }
                    sources.Add((keyContent, valueContent));
                }
            }
        }
        return sources;
    }

    private List<(string key, string feed)> GetManagedPackageSources(HashSet<string> feeds)
    {
        var sources = new List<(string key, string feed)>();

        foreach (var feed in feeds)
        {
            var parsedFeed = ParseMaestroManagedFeed(feed);

            var key = $"darc-{parsedFeed.type}-{parsedFeed.repoName}-{parsedFeed.sha.Substring(0, 7)}";
            if (!string.IsNullOrEmpty(parsedFeed.subVersion))
            {
                key += "-" + parsedFeed.subVersion;
            }
            sources.Add((key, feed));
        }
        return sources;
    }

    private (string org, string repoName, string type, string sha, string subVersion) ParseMaestroManagedFeed(string feed)
    {
        Match match = null;
        foreach (var pattern in FeedConstants.MaestroManagedFeedPatterns)
        {
            match = Regex.Match(feed, pattern);
            if (match.Success)
            {
                break;
            }
        }

        match = match.Success ?
            match :
            Regex.Match(feed, FeedConstants.AzureStorageProxyFeedPattern);

        if (match.Success)
        {
            var org = match.Groups["organization"].Value;
            var repo = match.Groups["repository"].Value;
            var type = match.Groups["type"].Value;
            var sha = match.Groups["sha"].Value;
            var subVersion = match.Groups["subversion"].Value;
            return (org, repo, type, sha, subVersion);
        }
        else
        {
            _logger.LogError($"Unable to parse feed {feed} as a Maestro managed feed");
            throw new ArgumentException($"feed {feed} is not a valid Maestro managed feed");
        }
    }

    private static Dictionary<string, HashSet<string>> GetAssetLocationMapping(IEnumerable<DependencyDetail> dependencies)
    {
        var assetLocationMappings = new Dictionary<string, HashSet<string>>();

        foreach (var dependency in dependencies)
        {
            if (!assetLocationMappings.ContainsKey(dependency.Name))
            {
                assetLocationMappings[dependency.Name] = [];
            }

            assetLocationMappings[dependency.Name].UnionWith(dependency.Locations ?? []);
        }

        return assetLocationMappings;
    }
}
