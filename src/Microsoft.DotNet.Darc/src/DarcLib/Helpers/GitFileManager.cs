// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace Microsoft.DotNet.DarcLib
{
    public class GitFileManager
    {
        private readonly IGitRepo _gitClient;
        private readonly ILogger _logger;

        private const string MaestroBeginComment =
            "Begin: Package sources managed by Dependency Flow automation. Do not edit the sources below.";

        private const string MaestroEndComment =
            "End: Package sources managed by Dependency Flow automation. Do not edit the sources above.";

        private const string MaestroRepoSpecificBeginComment = "  Begin: Package sources from";
        private const string MaestroRepoSpecificEndComment = "  End: Package sources from";

        public GitFileManager(IGitRepo gitRepo, ILogger logger)
        {
            _gitClient = gitRepo;
            _logger = logger;
        }

        public static HashSet<string> DependencyFiles =>
            new HashSet<string>
            {
                VersionFiles.VersionDetailsXml,
                VersionFiles.VersionProps,
                VersionFiles.GlobalJson,
                VersionFiles.DotnetToolsConfigJson
            };

        public async Task<XmlDocument> ReadVersionDetailsXmlAsync(string repoUri, string branch)
        {
            return await ReadXmlFileAsync(VersionFiles.VersionDetailsXml, repoUri, branch);
        }

        public XmlDocument ReadVersionDetailsXml(string fileContent)
        {
            return ReadXmlFile(fileContent);
        }

        public async Task<XmlDocument> ReadVersionPropsAsync(string repoUri, string branch)
        {
            return await ReadXmlFileAsync(VersionFiles.VersionProps, repoUri, branch);
        }

        public async Task<JObject> ReadGlobalJsonAsync(string repoUri, string branch)
        {
            _logger.LogInformation(
                $"Reading '{VersionFiles.GlobalJson}' in repo '{repoUri}' and branch '{branch}'...");

            string fileContent = await _gitClient.GetFileContentsAsync(VersionFiles.GlobalJson, repoUri, branch);

            return JObject.Parse(fileContent);
        }

        public async Task<JObject> ReadDotNetToolsConfigJsonAsync(string repoUri, string branch)
        {
            _logger.LogInformation(
                $"Reading '{VersionFiles.DotnetToolsConfigJson}' in repo '{repoUri}' and branch '{branch}'...");

            try
            {
                string fileContent = await _gitClient.GetFileContentsAsync(VersionFiles.DotnetToolsConfigJson, repoUri, branch);
                return JObject.Parse(fileContent);
            }
            catch (DependencyFileNotFoundException)
            {
                // Not exceptional: just means this repo doesn't have a .config/dotnet-tools.json, we'll skip the update.
                return null;
            }
        }

        public XmlDocument ReadNugetConfigAsync(string fileContent)
        {
            return ReadXmlFile(fileContent);
        }

        public async Task<XmlDocument> ReadNugetConfigAsync(string repoUri, string branch)
        {
            return await ReadXmlFileAsync(VersionFiles.NugetConfig, repoUri, branch);
        }

        public IEnumerable<DependencyDetail> ParseVersionDetailsXml(string fileContents, bool includePinned = true)
        {
            _logger.LogInformation($"Getting a collection of dependencies from '{VersionFiles.VersionDetailsXml}'...");

            XmlDocument document = ReadVersionDetailsXml(fileContents);

            return GetDependencyDetails(document, includePinned: includePinned);
        }

        public async Task<IEnumerable<DependencyDetail>> ParseVersionDetailsXmlAsync(string repoUri, string branch, bool includePinned = true)
        {
            if (!string.IsNullOrEmpty(branch))
            {
                _logger.LogInformation(
                    $"Getting a collection of dependencies from '{VersionFiles.VersionDetailsXml}' in repo '{repoUri}' " +
                    $"and branch '{branch}'...");
            }
            else
            {
                _logger.LogInformation(
                    $"Getting a collection of dependencies from '{VersionFiles.VersionDetailsXml}' in repo '{repoUri}'...");
            }

            XmlDocument document = await ReadVersionDetailsXmlAsync(repoUri, branch);

            return GetDependencyDetails(document, includePinned: includePinned);
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
            if ((await ParseVersionDetailsXmlAsync(repoUri, branch)).Any(
                existingDependency => existingDependency.Name.Equals(dependency.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DependencyException($"Dependency {dependency.Name} already exists in this repository");
            }

            if (DependencyOperations.TryGetKnownUpdater(dependency.Name, out Delegate function))
            {
                await (Task) function.DynamicInvoke(this, repoUri, branch, dependency);
            }
            else
            {
                await AddDependencyToVersionsPropsAsync(
                    repoUri,
                    branch,
                    dependency);
                await AddDependencyToVersionDetailsAsync(
                    repoUri,
                    branch,
                    dependency);
            }
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="itemsToUpdate"></param>
        /// <param name="repoUri"></param>
        /// <param name="branch"></param>
        /// <param name="oldDependencies"></param>
        /// <param name="incomingDotNetSdkVersion"></param>
        /// <returns></returns>
        public async Task<GitFileContentContainer> UpdateDependencyFiles(
            IEnumerable<DependencyDetail> itemsToUpdate,
            string repoUri,
            string branch,
            IEnumerable<DependencyDetail> oldDependencies,
            SemanticVersion incomingDotNetSdkVersion)
        {
            XmlDocument versionDetails = await ReadVersionDetailsXmlAsync(repoUri, branch);
            XmlDocument versionProps = await ReadVersionPropsAsync(repoUri, branch);
            JObject globalJson = await ReadGlobalJsonAsync(repoUri, branch);
            JObject toolsConfigurationJson = await ReadDotNetToolsConfigJsonAsync(repoUri, branch);
            XmlDocument nugetConfig = await ReadNugetConfigAsync(repoUri, branch);

            foreach (DependencyDetail itemToUpdate in itemsToUpdate)
            {
                if (string.IsNullOrEmpty(itemToUpdate.Version) ||
                    string.IsNullOrEmpty(itemToUpdate.Name) ||
                    string.IsNullOrEmpty(itemToUpdate.Commit) ||
                    string.IsNullOrEmpty(itemToUpdate.RepoUri))
                {
                    throw new DarcException($"Either the name, version, commit or repo uri of dependency '{itemToUpdate.Name}' in " +
                        $"repo '{repoUri}' and branch '{branch}' was empty.");
                }

                // Double check that the dependency is not pinned
                if (itemToUpdate.Pinned)
                {
                    throw new DarcException($"An attempt to update pinned dependency '{itemToUpdate.Name}' was made");
                }

                // Use a case-insensitive update.
                XmlNodeList versionList = versionDetails.SelectNodes($"//{VersionFiles.DependencyElementName}[translate(@Name,'ABCDEFGHIJKLMNOPQRSTUVWXYZ'," +
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

                SetAttribute(versionDetails, nodeToUpdate, VersionFiles.VersionAttributeName, itemToUpdate.Version);
                SetAttribute(versionDetails, nodeToUpdate, VersionFiles.NameAttributeName, itemToUpdate.Name);
                SetElement(versionDetails, nodeToUpdate, VersionFiles.ShaElementName, itemToUpdate.Commit);
                SetElement(versionDetails, nodeToUpdate, VersionFiles.UriElementName, itemToUpdate.RepoUri);
                UpdateVersionFiles(versionProps, globalJson, toolsConfigurationJson, itemToUpdate);
            }

            // Combine the two sets of dependencies. If an asset is present in the itemsToUpdate,
            // prefer that one over the old dependencies
            Dictionary<string, HashSet<string>> itemsToUpdateLocations = GetAssetLocationMapping(itemsToUpdate);

            if (oldDependencies != null)
            {
                foreach (DependencyDetail dependency in oldDependencies)
                {
                    if (!itemsToUpdateLocations.ContainsKey(dependency.Name) && dependency.Locations != null)
                    {
                        itemsToUpdateLocations.Add(dependency.Name, new HashSet<string>(dependency.Locations));
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
                if (SemanticVersion.TryParse(globalJson.SelectToken("tools.dotnet").ToString(), out SemanticVersion repoDotnetVersion))
                {
                    if (repoDotnetVersion.CompareTo(incomingDotnetVersion) < 0)
                    {
                        Dictionary<GitFileMetadataName, string> metadata = new Dictionary<GitFileMetadataName, string>();

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

        private bool IsOnlyPresentInMaestroManagedFeed(HashSet<string> locations)
        {
            return locations != null && locations.All(l => IsMaestroManagedFeed(l));
        }

        private bool IsMaestroManagedFeed(string feed)
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
            bool withinMaestroComments = false;

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
                    else if (currentNode.Name.Equals(VersionFiles.ClearElement, StringComparison.OrdinalIgnoreCase))
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
                    // while we traverse, we may as well remove all the existing entries for what we're updating.
                    if (currentNode.Name.Equals("add", StringComparison.InvariantCultureIgnoreCase) &&
                        currentNode.Attributes["key"]?.Value.StartsWith(disableFeedKeyPrefix) == true &&
                        // If there somehow is an unrelated darc-* source entry in here, we'll leave it alone.
                        allPossibleManagedSources.Any(ms => ms == currentNode.Attributes["key"]?.Value))
                    {
                        currentNode = RemoveCurrentNode(currentNode);
                        continue;
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
            bool introducedAStartCommentBlock = false;

            foreach (string repoName in maestroManagedFeedsByRepo.Keys.OrderBy(t => t))
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
                    startCommentBlock = (XmlComment) insertAfterNode;
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
                endCommentBlock = (XmlComment) disabledSourcesNode.InsertAfter(nugetConfig.CreateComment(MaestroEndComment), insertAfterNode);
            }
        }

        // Insert the following structure at the beginning of the nodes pointed by `packageSourcesNode`.
        // <clear/>
        // <MaestroBeginComment />
        // managedSources*
        // <MaestroEndComment />
        private void InsertManagedPackagesBlock(XmlDocument nugetConfig, XmlNode packageSourcesNode, Dictionary<string, HashSet<string>> maestroManagedFeedsByRepo)
        {
            var clearNode = nugetConfig.CreateElement(VersionFiles.ClearElement);
            XmlNode currentNode = packageSourcesNode.PrependChild(clearNode);

            if (maestroManagedFeedsByRepo.Values.Count == 0)
            {
                return;
            }

            var repoList = maestroManagedFeedsByRepo.Keys.OrderBy(t => t).ToList();

            XmlComment blockBeginComment = GetFirstMatchingComment(packageSourcesNode, MaestroBeginComment);
            if (blockBeginComment == null)
            {
                blockBeginComment = (XmlComment) packageSourcesNode.InsertAfter(nugetConfig.CreateComment(MaestroBeginComment), clearNode);
            }
            currentNode = blockBeginComment;

            foreach (string repository in repoList)
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

                foreach ((string key, string feed) in managedSources)
                {
                    var newElement = nugetConfig.CreateElement(VersionFiles.AddElement);

                    SetAttribute(nugetConfig, newElement, VersionFiles.KeyAttributeName, key);
                    SetAttribute(nugetConfig, newElement, VersionFiles.ValueAttributeName, feed);

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

        private XmlComment GetFirstMatchingComment(XmlNode nodeToCheck, string commentText)
        {
            if (nodeToCheck.HasChildNodes)
            {
                XmlNode currentNode = nodeToCheck.FirstChild;

                while (currentNode != null)
                {
                    if (currentNode.NodeType == XmlNodeType.Comment &&
                       (currentNode.Value.Equals(commentText, StringComparison.OrdinalIgnoreCase)))
                    {
                        return (XmlComment) currentNode;
                    }
                    currentNode = currentNode.NextSibling;
                }
            }
            return null;
        }

        public async Task AddDependencyToVersionDetailsAsync(
            string repo,
            string branch,
            DependencyDetail dependency)
        {
            XmlDocument versionDetails = await ReadVersionDetailsXmlAsync(repo, null);

            XmlNode newDependency = versionDetails.CreateElement(VersionFiles.DependencyElementName);

            SetAttribute(versionDetails, newDependency, VersionFiles.NameAttributeName, dependency.Name);
            SetAttribute(versionDetails, newDependency, VersionFiles.VersionAttributeName, dependency.Version);

            // Only add the pinned attribute if the pinned option is set to true
            if (dependency.Pinned)
            {
                SetAttribute(versionDetails, newDependency, VersionFiles.PinnedAttributeName, "True");
            }

            // Only add the coherent parent attribute if it is set
            if (!string.IsNullOrEmpty(dependency.CoherentParentDependencyName))
            {
                SetAttribute(versionDetails, newDependency, VersionFiles.CoherentParentAttributeName, dependency.CoherentParentDependencyName);
            }

            SetElement(versionDetails, newDependency, VersionFiles.UriElementName, dependency.RepoUri);
            SetElement(versionDetails, newDependency, VersionFiles.ShaElementName, dependency.Commit);

            XmlNode dependenciesNode = versionDetails.SelectSingleNode($"//{dependency.Type}{VersionFiles.DependenciesElementName}");
            if (dependenciesNode == null)
            {
                dependenciesNode = versionDetails.CreateElement($"{dependency.Type}{VersionFiles.DependenciesElementName}");
                versionDetails.DocumentElement.AppendChild(dependenciesNode);
            }
            dependenciesNode.AppendChild(newDependency);

            // TODO: This should not be done here.  This should return some kind of generic file container to the caller,
            // who will gather up all updates and then call the git client to write the files all at once:
            // https://github.com/dotnet/arcade/issues/1095.  Today this is only called from the Local interface so 
            // it's okay for now.
            var file = new GitFile(VersionFiles.VersionDetailsXml, versionDetails);
            await _gitClient.CommitFilesAsync(new List<GitFile> { file }, repo, branch, $"Add {dependency} to " +
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
        ///     See https://github.com/dotnet/arcade/blob/master/Documentation/DependencyDescriptionFormat.md for more
        ///     information.
        /// </summary>
        /// <param name="repo">Path to Versions.props file</param>
        /// <param name="dependency">Dependency information to add.</param>
        /// <returns>Async task.</returns>
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
            if (existingVersionNode == null)
            {
                existingVersionNode = versionProps.DocumentElement.SelectSingleNode($"//*[local-name()='{packageVersionAlternateElementName}' and parent::PropertyGroup]");
            }

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
                            if (!addedPackageVersionElement && propertyNode.Name.EndsWith(VersionFiles.VersionPropsVersionElementSuffix))
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
                                VersionFiles.VersionPropsAlternateVersionElementSuffix))
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
            await _gitClient.CommitFilesAsync(new List<GitFile> { file }, repo, branch, $"Add {dependency} to " +
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
            await _gitClient.CommitFilesAsync(new List<GitFile> { file }, repo, branch, $"Add {dependencyName} to " +
                $"'{VersionFiles.GlobalJson}'");

            _logger.LogInformation(
                $"Dependency '{dependencyName}' with version '{version}' successfully added to global.json");
        }

        public static XmlDocument ReadXmlFile(string fileContent)
        {
            return GetXmlDocument(fileContent);
        }

        public static XmlDocument GetXmlDocument(string fileContent)
        {
            XmlDocument document = new XmlDocument
            {
                PreserveWhitespace = true
            };
            document.LoadXml(fileContent);

            return document;
        }

        private async Task<XmlDocument> ReadXmlFileAsync(string filePath, string repoUri, string branch)
        {
            _logger.LogInformation($"Reading '{filePath}' in repo '{repoUri}' and branch '{branch}'...");

            string fileContent = await _gitClient.GetFileContentsAsync(filePath, repoUri, branch);

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
            string versionElementName = VersionFiles.GetVersionPropsPackageVersionElementName(itemToUpdate.Name);
            string alternateVersionElementName = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(itemToUpdate.Name);

            // Select nodes case insensitively, then update the name.
            XmlNode packageVersionNode = versionProps.DocumentElement.SelectSingleNode(
                $"//*[translate(local-name(),'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')=" +
                $"'{versionElementName.ToLower()}']");
            string foundElementName = versionElementName;

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

        private void UpdateVersionGlobalJson(DependencyDetail itemToUpdate, JToken token)
        {
            string versionElementName = VersionFiles.CalculateGlobalJsonElementName(itemToUpdate.Name);

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
            string versionElementName = itemToUpdate.Name;

            JObject toolsNode = (JObject) token["tools"];

            foreach (JProperty property in toolsNode?.Children<JProperty>())
            {
                if (property.Name.Equals(versionElementName, StringComparison.OrdinalIgnoreCase))
                {
                    JValue versionEntry = (JValue) property.Value.SelectToken("version", false);

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
            Task<IEnumerable<DependencyDetail>> dependencyDetails;
            Task<XmlDocument> versionProps;
            Task<JObject> globalJson;
            Task<JObject> dotnetToolsJson;

            try
            {
                dependencyDetails = ParseVersionDetailsXmlAsync(repo, branch);
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
                globalJson = ReadGlobalJsonAsync(repo, branch);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to read {VersionFiles.GlobalJson}");
                return false;
            }

            try
            {
                dotnetToolsJson = ReadDotNetToolsConfigJsonAsync(repo, branch);
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed to read {VersionFiles.DotnetToolsConfigJson}");
                return false;
            }

            List<Task<bool>> verificationTasks = new List<Task<bool>>()
            {
                VerifyNoDuplicatedProperties(await versionProps),
                VerifyNoDuplicatedDependencies(await dependencyDetails),
                VerifyMatchingVersionProps(
                    await dependencyDetails,
                    await versionProps,
                    out Task<HashSet<string>> utilizedVersionPropsDependencies),
                VerifyMatchingGlobalJson(
                    await dependencyDetails,
                    await globalJson,
                    out Task<HashSet<string>> utilizedGlobalJsonDependencies),
                VerifyUtilizedDependencies(
                    await dependencyDetails,
                    new List<HashSet<string>>
                    {
                        await utilizedVersionPropsDependencies,
                        await utilizedGlobalJsonDependencies
                    }),
                VerifyMatchingDotNetToolsJson(
                    await dependencyDetails,
                    await dotnetToolsJson)
            };

            var results = await Task.WhenAll<bool>(verificationTasks);
            return results.All(result => result);
        }

        public static void NormalizeAttributes(string directoryPath)
        {
            string[] filePaths = Directory.GetFiles(directoryPath);
            string[] subdirectoryPaths = Directory.GetDirectories(directoryPath);

            foreach (string filePath in filePaths)
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            foreach (string subdirectoryPath in subdirectoryPaths)
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
            bool hasNoDuplicatedProperties = true;
            HashSet<string> existingProperties = new HashSet<string>();

            XmlNodeList propertyGroups = versionProps.GetElementsByTagName("PropertyGroup");
            foreach (XmlNode propertyGroup in propertyGroups)
            {
                foreach (var property in propertyGroup.ChildNodes)
                {
                    if (property is XmlElement)
                    {
                        var element = property as XmlElement;
                        var propertyName = element.Name;

                        propertyName = Regex.Replace(propertyName, @"PackageVersion$", String.Empty);
                        propertyName = Regex.Replace(propertyName, @"Version$", String.Empty);

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
            bool result = true;
            HashSet<string> dependenciesBitVector = new HashSet<string>();
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
            HashSet<string> utilizedSet = new HashSet<string>();
            bool result = true;
            foreach (var dependency in dependencies)
            {
                string versionElementName = VersionFiles.GetVersionPropsPackageVersionElementName(dependency.Name);
                string alternateVersionElementName = VersionFiles.GetVersionPropsAlternatePackageVersionElementName(dependency.Name);
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
            HashSet<string> utilizedSet = new HashSet<string>();
            bool result = true;
            foreach (var dependency in dependencies)
            {
                string versionedName = VersionFiles.CalculateGlobalJsonElementName(dependency.Name);
                JToken dependencyNode = FindDependency(rootToken, versionedName);
                if (dependencyNode != null)
                {
                    // Should be a string with matching version.
                    if (dependencyNode.Type != JTokenType.Property || ((JProperty) dependencyNode).Value.Type != JTokenType.String)
                    {
                        _logger.LogError($"The element '{dependency.Name}' in '{VersionFiles.GlobalJson}' should be a property " +
                            $"with a value of type string.");
                        result = false;
                        continue;
                    }
                    JProperty property = (JProperty) dependencyNode;
                    // Validate that the casing matches for consistency
                    if (property.Name != versionedName)
                    {
                        _logger.LogError($"The dependency '{dependency.Name}' has a case mismatch between " +
                            $"'{VersionFiles.GlobalJson}' and '{VersionFiles.VersionDetailsXml}' " +
                            $"('{property.Name}' vs. '{versionedName}')");
                        result = false;
                    }
                    // Validate version
                    JToken value = (JToken) property.Value;
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
            bool result = true;
            // If there isn't a .config/dotnet-tools.json, skip checking
            if (rootToken != null)
            {
                foreach (var dependency in dependencies)
                {
                    string versionedName = VersionFiles.CalculateDotnetToolsJsonElementName(dependency.Name);
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

                        JProperty property = (JProperty) dependencyNode;
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
        private JToken FindDependency(JToken currentToken, string elementName)
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
            bool result = true;
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

        private IEnumerable<DependencyDetail> GetDependencyDetails(XmlDocument document, bool includePinned = true)
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
                _logger.LogError($"There was an error while reading '{VersionFiles.VersionDetailsXml}' and it came back empty. " +
                    $"Look for exceptions above.");
            }

            if (includePinned)
            {
                return dependencyDetails;
            }

            return dependencyDetails.Where(d => !d.Pinned);
        }

        /// <summary>
        ///  Infer repo names from feeds using regex.
        ///  If any feed name resolution fails, we'll just put it into an "unknown" bucket.
        /// </summary>
        /// <param name="assetLocationMap">Dictionary of all feeds by their location</param>
        /// <returns>Dictionary with key = repo name for logging, value = hashset of feeds</returns>
        public Dictionary<string, HashSet<string>> FlattenLocationsAndSplitIntoGroups(Dictionary<string, HashSet<string>> assetLocationMap)
        {
            HashSet<string> allManagedFeeds = new HashSet<string>();
            foreach (string asset in assetLocationMap.Keys)
            {
                if (IsOnlyPresentInMaestroManagedFeed(assetLocationMap[asset]))
                {
                    allManagedFeeds.UnionWith(assetLocationMap[asset]);
                }
            }

            string unableToResolveName = "unknown";
            Dictionary<string, HashSet<string>> result = new Dictionary<string, HashSet<string>>();
            foreach (string feedUri in allManagedFeeds)
            {
                string repoNameFromFeed = string.Empty;
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
                    _logger.LogError("Unable to use regex to determine repo information from feed", e);
                    repoNameFromFeed = unableToResolveName;
                }

                if (!result.ContainsKey(repoNameFromFeed))
                {
                    result.Add(repoNameFromFeed, new HashSet<string>());
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

            foreach (string feed in feeds)
            {
                var parsedFeed = ParseMaestroManagedFeed(feed);

                string key = $"darc-{parsedFeed.type}-{parsedFeed.repoName}-{parsedFeed.sha.Substring(0, 7)}";
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
            foreach (string pattern in FeedConstants.MaestroManagedFeedPatterns)
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
                string org = match.Groups["organization"].Value;
                string repo = match.Groups["repository"].Value;
                string type = match.Groups["type"].Value;
                string sha = match.Groups["sha"].Value;
                string subVersion = match.Groups["subversion"].Value;
                return (org, repo, type, sha, subVersion);
            }
            else
            {
                _logger.LogError($"Unable to parse feed { feed } as a Maestro managed feed");
                throw new ArgumentException($"feed { feed } is not a valid Maestro managed feed");
            }
        }

        private Dictionary<string, HashSet<string>> GetAssetLocationMapping(IEnumerable<DependencyDetail> dependencies)
        {
            var assetLocationMappings = new Dictionary<string, HashSet<string>>();

            foreach (var dependency in dependencies)
            {
                if (!assetLocationMappings.ContainsKey(dependency.Name))
                {
                    assetLocationMappings[dependency.Name] = new HashSet<string>();
                }

                assetLocationMappings[dependency.Name].UnionWith(dependency.Locations ?? Enumerable.Empty<string>());
            }

            return assetLocationMappings;
        }
    }
}
