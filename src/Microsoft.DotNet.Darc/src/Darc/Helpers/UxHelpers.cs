// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc
{
    public static class UxHelpers
    {
        /// <summary>
        ///     Resolve a channel name into a single channel.
        /// </summary>
        /// <param name="channels">List of channels</param>
        /// <param name="desiredChannel">Desired channel</param>
        /// <returns>Channel, or null if no channel was matched.</returns>
        public static Channel ResolveSingleChannel(IEnumerable<Channel> channels, string desiredChannel)
        {
            // Retrieve the channel by name, matching substring. If more than one channel 
            // matches (and none is an exact match), then let the user know they need to be more specific
            IEnumerable<Channel> matchingChannels = channels.Where(c => c.Name.Contains(desiredChannel, StringComparison.OrdinalIgnoreCase));

            if (!matchingChannels.Any())
            {
                Console.WriteLine($"No channels found with name containing '{desiredChannel}'");
                Console.WriteLine("Available channels:");
                foreach (Channel channel in channels)
                {
                    Console.WriteLine($"  {channel.Name}");
                }
                return null;
            }
            else if (matchingChannels.Count() != 1)
            {
                Channel exactMatchingChannel = matchingChannels
                    .Where(c => c.Name.Equals(desiredChannel, StringComparison.OrdinalIgnoreCase))
                    .SingleOrDefault();

                if (exactMatchingChannel != null)
                {
                    return exactMatchingChannel;
                }

                Console.WriteLine($"Multiple channels found with name containing '{desiredChannel}', please select one");
                foreach (Channel channel in matchingChannels)
                {
                    Console.WriteLine($"  {channel.Name}");
                }
                return null;
            }
            else
            {
                return matchingChannels.Single();
            }
        }

        /// <summary>
        ///     Resolve a channel substring to an exact channel, or print out potential names if more than one, or none, match.
        /// </summary>
        /// <param name="remote">Remote for retrieving channels</param>
        /// <param name="desiredChannel">Desired channel</param>
        /// <returns>Channel, or null if no channel was matched.</returns>
        public static async Task<Channel> ResolveSingleChannel(IRemote remote, string desiredChannel)
        {
            return ResolveSingleChannel(await remote.GetChannelsAsync(), desiredChannel);
        }

        public static string GetSubscriptionDescription(Subscription subscription)
        {
            return $"{subscription.SourceRepository} ({subscription.Channel.Name}) ==> '{subscription.TargetRepository}' ('{subscription.TargetBranch}')";
        }

        /// <summary>
        ///     Get a string description of a build.
        /// </summary>
        /// <param name="build">Build</param>
        /// <returns>Description</returns>
        public static string GetBuildDescription(Build build)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"Repository:    {build.GitHubRepository ?? build.AzureDevOpsRepository}");
            builder.AppendLine($"Branch:        {build.GitHubBranch ?? build.AzureDevOpsBranch}");
            builder.AppendLine($"Commit:        {build.Commit}");
            builder.AppendLine($"Build Number:  {build.AzureDevOpsBuildNumber}");
            builder.AppendLine($"Date Produced: {build.DateProduced.ToLocalTime().ToString("g")}");
            if (!string.IsNullOrEmpty(build.AzureDevOpsAccount) &&
                !string.IsNullOrEmpty(build.AzureDevOpsProject) &&
                build.AzureDevOpsBuildId.HasValue)
            {
                string azdoLink = $"https://dev.azure.com/{build.AzureDevOpsAccount}/{build.AzureDevOpsProject}/_build/results?buildId={build.AzureDevOpsBuildId.Value}";
                builder.AppendLine($"Build Link:    {azdoLink}");
            }
            builder.AppendLine($"BAR Build Id:  {build.Id}");
            builder.AppendLine($"Released:      {build.Released}");
            if (build.Channels != null)
            {
                builder.AppendLine($"Channels:");
                foreach (Channel buildChannel in build.Channels)
                {
                    builder.AppendLine($"- {buildChannel.Name}");
                }
            }

            return builder.ToString();
        }

        /// <summary>
        ///     Get a description string of a set of merge policies, if any.
        /// </summary>
        /// <param name="mergePolicies">Merge policies</param>
        /// <param name="indent">Indentation of lines</param>
        /// <returns>Description string</returns>
        public static string GetMergePoliciesDescription(IEnumerable<MergePolicy> mergePolicies, string indent = "")
        {
            StringBuilder builder = new StringBuilder();

            if (mergePolicies.Any())
            {
                builder.AppendLine($"{indent}- Merge Policies:");
                foreach (MergePolicy mergePolicy in mergePolicies)
                {
                    builder.AppendLine($"{indent}  {mergePolicy.Name}");
                    if (mergePolicy.Properties != null)
                    {
                        foreach (var mergePolicyProperty in mergePolicy.Properties)
                        {
                            // The merge policy property is a key value pair.  For formatting, turn it into a string.
                            // It's often a JToken, so handle appropriately
                            // 1. If the number of lines in the string is 1, write on same line as key
                            // 2. If the number of lines in the string is more than one, start on new
                            //    line and indent.
                            string valueString = mergePolicyProperty.Value.ToString();
                            string[] valueLines = valueString.Split(System.Environment.NewLine);
                            string keyString = $"{indent}    {mergePolicyProperty.Key} = ";
                            builder.Append(keyString);
                            if (valueLines.Length == 1)
                            {
                                builder.AppendLine(valueString);
                            }
                            else
                            {
                                string indentString = new string(' ', keyString.Length);
                                builder.AppendLine();
                                foreach (string line in valueLines)
                                {
                                    builder.AppendLine($"{indent}{indentString}{line}");
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                builder.AppendLine($"{indent}- Merge Policies: []");
            }

            return builder.ToString();
        }

        public static string GetSimpleRepoName(string repoUri)
        {
            int lastSlash = repoUri.LastIndexOf("/");
            if ((lastSlash != -1) && (lastSlash < (repoUri.Length - 1)))
            {
                return repoUri.Substring(lastSlash + 1);
            }
            return repoUri;
        }

        /// <summary>
        ///     Calculate a valid graphviz node name for a dependency graph node
        /// </summary>
        /// <param name="node">Node</param>
        /// <returns>Valid node name string with repo name and commit</returns>
        public static string CalculateGraphVizNodeName(DependencyGraphNode node)
        {
            return CalculateGraphVizNodeName(GetSimpleRepoName(node.Repository)) + node.Commit;
        }

        /// <summary>
        ///     Calculate a validate graphviz node name for a dependency flow node
        /// </summary>
        /// <param name="node">Node</param>
        /// <returns>Valid node name string with repo and branch</returns>
        public static string CalculateGraphVizNodeName(DependencyFlowNode node)
        {
            return CalculateGraphVizNodeName(GetSimpleRepoName(node.Repository) + node.Branch);
        }

        /// <summary>
        ///     Calculate a valid graphviz node name off of an input name string
        /// </summary>
        /// <param name="name">Input string</param>
        /// <returns>String with invalid characters replaced</returns>
        public static string CalculateGraphVizNodeName(string name)
        {
            return name.Replace(".", "")
                       .Replace("-", "")
                       .Replace("/", "")
                       .Replace(" ", "");
        }

        /// <summary>
        ///     Get a string for describing a default channel.
        /// </summary>
        /// <param name="defaultChannel">Default channel to get a string for</param>
        /// <returns>String describing the default channel.</returns>
        public static string GetDefaultChannelDescriptionString(DefaultChannel defaultChannel)
        {
            string disabled = !defaultChannel.Enabled ? " (Disabled)" : "";
            // Pad so that id's up to 9999 will result in consistent
            // listing
            string idPrefix = $"({defaultChannel.Id})".PadRight(7);
            return $"{idPrefix}{defaultChannel.Repository} @ {defaultChannel.Branch} -> {defaultChannel.Channel.Name}{disabled}";
        }

        /// <summary>
        ///     Retrieve either a new StreamWriter for the specified output file,
        ///     or if the output file name is empty, create a new StreamWriter
        ///     wrapping standard out.
        /// </summary>
        /// <param name="outputFile">Output file name.</param>
        /// <returns>New stream writer</returns>
        /// <remarks>
        ///     The StreamWriter can be disposed of even if it's wrapping Console.Out.
        ///     The underlying stream is only disposed of if the stream writer owns it.
        /// </remarks>
        public static StreamWriter GetOutputFileStreamOrConsole(string outputFile)
        {
            StreamWriter outputStream = null;
            if (!string.IsNullOrEmpty(outputFile))
            {
                string fullPath = Path.GetFullPath(outputFile);
                string directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                outputStream = new StreamWriter(fullPath);
            }
            else
            {
                outputStream = new StreamWriter(Console.OpenStandardOutput());
            }
            return outputStream;
        }

        /// <summary>
        ///     Verify that a branch exists in the specified repo and contains a version details file.
        ///     If it does not, optionally prompt the user to confirm that they wish to continue.
        /// </summary>
        /// <param name="remote">Remote</param>
        /// <param name="repo">Repository that the branch should be in</param>
        /// <param name="branch">Branch to check the existence of</param>
        /// <param name="prompt">Prompt the user to verify that they want to continue</param>
        /// <returns>True if the branch exists, prompting is not desired, or if the user confirms that they want to continue. False otherwise.</returns>
        public static async Task<bool> VerifyAndConfirmBranchExistsAsync(IRemote remote, string repo, string branch, bool prompt)
        {
            try
            {
                if (!(await VerifyAndConfirmRepositoryExistsAsync(remote, repo, false)))
                {
                    return false;
                }

                await remote.GetDependenciesAsync(repo, branch);
            }
            catch (DependencyFileNotFoundException)
            {
                Console.WriteLine($"Warning: Could not find an eng/Version.Details.xml at '{repo}@{branch}'. Dependency updates may not happen as expected.");
                if (prompt)
                {
                    return PromptForYesNo("Continue?");
                }
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Verify that a repository exists, and optionally prompt the user if it does not.
        /// </summary>
        /// <param name="remote">Remote</param>
        /// <param name="repo">Repository to check for</param>
        /// <param name="prompt">Prompt the user to verify that they want to continue</param>
        /// <returns>True if the repository exists, prompting is not desired, or if the user confirms that they want to continue. False otherwise.</returns>
        public static async Task<bool> VerifyAndConfirmRepositoryExistsAsync(IRemote remote, string repo, bool prompt)
        {
            if (!(await remote.RepositoryExistsAsync(repo)))
            {
                Console.WriteLine($"Warning: Could not locate repository '{repo}'. Dependency updates may not happen as expected.");
                if (prompt)
                {
                    return PromptForYesNo("Continue?");
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Prompt the user to continue or not
        /// </summary>
        /// <param name="message">Message to print before asking for input.</param>
        /// <returns>True if we should continue, false otherwise.</returns>
        public static bool PromptForYesNo(string message)
        {
            char keyChar;
            int triesRemaining = 3;
            do
            {
                if (triesRemaining == 0)
                {
                    // Don't continue if the user can't press y or n.
                    Console.Write("Invalid input, aborting.");
                    return false;
                }
                triesRemaining--;

                Console.Write($"{message} (y/n) ");
                ConsoleKeyInfo keyInfo = Console.ReadKey();
                keyChar = char.ToUpperInvariant(keyInfo.KeyChar);
                Console.WriteLine();
            }
            while (keyChar != 'Y' && keyChar != 'N');

            return keyChar == 'Y';
        }
    }
}
