// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using System;

namespace Microsoft.DotNet.Darc
{
    public static class OutputFormattingHelpers
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
    }
}
