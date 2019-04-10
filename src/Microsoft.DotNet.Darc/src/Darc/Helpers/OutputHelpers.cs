// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.IO;

namespace Microsoft.DotNet.Darc
{
    public static class OutputHelpers
    {
        public static void PrintBuild(Build build)
        {
            Console.WriteLine($"Repository:    {build.GitHubRepository ?? build.AzureDevOpsRepository}");
            Console.WriteLine($"Branch:        {build.GitHubBranch ?? build.AzureDevOpsBranch}");
            Console.WriteLine($"Commit:        {build.Commit}");
            Console.WriteLine($"Build Number:  {build.AzureDevOpsBuildNumber}");
            Console.WriteLine($"Date Produced: {build.DateProduced.ToLocalTime().ToString("g")}");
            if (!string.IsNullOrEmpty(build.AzureDevOpsAccount) &&
                !string.IsNullOrEmpty(build.AzureDevOpsProject) &&
                build.AzureDevOpsBuildId.HasValue)
            {
                string azdoLink = $"https://dev.azure.com/{build.AzureDevOpsAccount}/{build.AzureDevOpsProject}/_build/results?buildId={build.AzureDevOpsBuildId.Value}";
                Console.WriteLine($"Build Link:    {azdoLink}");
            }
            Console.WriteLine($"BAR Build Id:  {build.Id}");
            Console.WriteLine($"Channels:");
            foreach (Channel buildChannel in build.Channels)
            {
                Console.WriteLine($"- {buildChannel.Name}");
            }
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
        public static string CalculateGraphVizNodeName(DependencyGraphNode node)
        {
            return CalculateGraphVizNodeName(GetSimpleRepoName(node.Repository)) + node.Commit;
        }
        public static string CalculateGraphVizNodeName(DependencyFlowNode node)
        {
            return CalculateGraphVizNodeName(GetSimpleRepoName(node.Repository) + node.Branch);
        }
        public static string CalculateGraphVizNodeName(string name)
        {
            return name.Replace(".", "")
                       .Replace("-", "")
                       .Replace("/", "")
                       .Replace(" ", "");
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
    }
}
