// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    /// <summary>
    /// Mock Azure DevOps client that is initialized with backing data.
    /// </summary>
    public class MockAzureClient : IAzureDevOpsClient
    {
        public IDictionary<Build, List<Timeline>> Builds { get; }

        public static string oneESImageName = "Build.Ubuntu.1804.Amd64";
        public static string microsoftHostedAgentImageName = "windows-2019";
        public static string oneESLogUrl = $"https://www.fakeurl.com/{oneESImageName}";
        public static string microsoftHostedAgentLogUrl = $"https://www.fakeurl.com/{microsoftHostedAgentImageName}";
        public static string oneESLog = @$"SKU: Standard_D4_v3
Image: {oneESImageName}
Image Version: 2022.0219.013407";
        public static string microsoftHostedLog = @$"SKU: Standard_D4_v3
Environment: {microsoftHostedAgentImageName}
Version: 20220223.1";

        private readonly IDictionary<string, string> _urlDictionary;

        public MockAzureClient()
        {
            Builds = new Dictionary<Build, List<Timeline>>();
        }

        public MockAzureClient(Dictionary<Build, List<Timeline>> builds)
        {
            Builds = builds;
            _urlDictionary = new Dictionary<string, string>()
            {
                { oneESLogUrl, oneESLog },
                { microsoftHostedAgentLogUrl, microsoftHostedLog }
            };
        }

        public Task<Build[]> ListBuilds(string project, CancellationToken cancellationToken, DateTimeOffset? minTime = null, int? limit = null)
        {
            return Task.FromResult(Builds.Keys.ToArray());
        }

        public Task<Timeline> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Builds
                .Single(build => build.Key.Id == buildId && build.Key.Project.Name == project)
                .Value.OrderByDescending(timeline => timeline.LastChangedOn).First());
        }

        public Task<Timeline> GetTimelineAsync(string project, int buildId, string timelineId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Builds
                .Single(build => build.Key.Id == buildId && build.Key.Project.Name == project)
                .Value.Single(timeline => timeline.Id == timelineId));
        }

        public Task<Build> GetBuildAsync(string project, long buildId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(BuildChange[] changes, int truncatedChangeCount)> GetBuildChangesAsync(string project, long buildId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<BuildChangeDetail> GetChangeDetails(string changeUrl, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<AzureDevOpsProject[]> ListProjectsAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<WorkItem> CreateRcaWorkItem(string project, string title, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<string> TryGetLogContents(string logUri, CancellationToken cancellationToken)
        {
            return Task.FromResult(_urlDictionary[logUri]);
        }
    }
}
