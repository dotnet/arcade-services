// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Kusto.Cloud.Platform.Utils;
using Microsoft.DotNet.Internal.AzureDevOps;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests;

/// <summary>
/// Mock Azure DevOps client that is initialized with backing data.
/// </summary>
public class MockAzureClient : IAzureDevOpsClient
{
    public IDictionary<Build, List<Timeline>> Builds { get; }

    public static string OneESImageName = "Build.Ubuntu.1804.Amd64";
    public static string MicrosoftHostedAgentImageName = "windows-2019";
    public static string MmsOneESImageName = "1es-windows-2019";
    public static string OneESLogUrl = $"https://www.fakeurl.test/{OneESImageName}";
    public static string MicrosoftHostedAgentLogUrl = $"https://www.fakeurl.test/{MicrosoftHostedAgentImageName}";
    public static string MmsOneESLogUrl = $"https://www.fakeurl.test/{MmsOneESImageName}";
    public static string OneESLog = $"""
        2023-04-05T10:02:04.2858589Z ##[group]1ES Hosted Pool
        2023-04-05T10:02:04.2858784Z SKU: Standard_D4a_v4
        2023-04-05T10:02:04.2858877Z Image: {OneESImageName}
        """;
    public static string MicrosoftHostedLog = $"""
        2023-04-05T08:34:15.6959600Z ##[group]Runner Image
        2023-04-05T08:34:15.6959721Z Image: {MicrosoftHostedAgentImageName}
        2023-04-05T08:34:15.6959819Z Version: 20230402.1
        """;
    public static string MmsOneESLog = $"""
        2023-04-04T15:10:06.5649001Z ##[group]Runner Image
        2023-04-04T15:10:06.5649079Z Image: {MicrosoftHostedAgentImageName}
        2023-04-04T15:10:06.5649151Z Version: 20230326.1
        2023-04-04T15:10:06.5649290Z Included Software: https://github.com/actions/runner-images/blob/win22/20230326.1/images/win/Windows2022-Readme.md
        2023-04-04T15:10:06.5649490Z Image Release: https://github.com/actions/runner-images/releases/tag/win22%2F20230326.1
        2023-04-04T15:10:06.5649608Z ##[endgroup]
        2023-04-04T15:10:06.5649673Z ##[group]1ES Hosted Pool
        2023-04-04T15:10:06.5649938Z SKU: Standard_D4a_v4
        2023-04-04T15:10:06.5650033Z Image: {MmsOneESImageName}
        2023-04-04T15:10:06.5650108Z Image Version: 60.0.0
    """;
    public static string DockerImageName = "mcr.microsoft.com/dotnet-buildtools/prereqs:centos-7-mlnet-8bba86b-20190314145033";
    public static string DockerLogUrl = $"https://www.fakeurl.test/{DockerImageName}";
    public static string DockerLog = $"""
        2022-08-04T08:00:55.3397230Z Docker client API version: '1.41'
        2022-08-04T08:00:55.3410985Z ##[command]/usr/bin/docker ps --all --quiet --no-trunc --filter 'label=82ff57'
        2022-08-04T08:00:55.3863389Z ##[command]/usr/bin/docker network prune --force --filter 'label=82ff57'
        2022-08-04T08:00:55.4450968Z ##[command]/usr/bin/docker pull {DockerImageName}
        2022-08-04T08:00:55.8023661Z centos-7-mlnet-8bba86b-20190314145033: Pulling from dotnet-buildtools/prereqs
        2022-08-04T08:00:55.8024272Z a02a4930cb5d: Pulling fs layer
        2022-08-04T08:00:55.8024527Z 54355103c1c9: Pulling fs layer
    """;

    private readonly IDictionary<string, string?> _urlDictionary;

    public MockAzureClient()
    {
        Builds = new Dictionary<Build, List<Timeline>>();
        _urlDictionary = new Dictionary<string, string?>()
        {
            { OneESLogUrl, OneESImageName },
            { MicrosoftHostedAgentLogUrl, MicrosoftHostedAgentImageName},
            { DockerLogUrl, DockerImageName },
        };
    }

    public MockAzureClient(Dictionary<Build, List<Timeline>> builds)
    {
        Builds = builds;
        _urlDictionary = new Dictionary<string, string?>()
        {
            { OneESLogUrl, OneESImageName },
            { MicrosoftHostedAgentLogUrl, MicrosoftHostedAgentImageName},
            { DockerLogUrl, DockerImageName },
        };
    }

    public Task<Build[]> ListBuilds(string project, CancellationToken cancellationToken, DateTimeOffset? minTime = null, int? limit = null)
    {
        return Task.FromResult(Builds.Keys.ToArray());
    }

    public Task<Timeline?> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Builds
            .Single(build => build.Key.Id == buildId && build.Key.Project.Name == project)
            .Value.OrderByDescending(timeline => timeline.LastChangedOn).FirstOrDefault());
    }

    public Task<Timeline?> GetTimelineAsync(string project, int buildId, string timelineId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Builds
            .Single(build => build.Key.Id == buildId && build.Key.Project.Name == project)
            .Value.SingleOrDefault(timeline => timeline.Id == timelineId));
    }

    public Task<Build?> GetBuildAsync(string project, long buildId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<(BuildChange[]? changes, int? truncatedChangeCount)?> GetBuildChangesAsync(string project, long buildId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<BuildChangeDetail?> GetChangeDetails(string changeUrl, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Internal.AzureDevOps.AzureDevOpsProject[]?> ListProjectsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<WorkItem?> CreateRcaWorkItem(string project, string title, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<string?> MatchLogLineSequence(string logUri, IReadOnlyList<Regex> regexes, CancellationToken cancellationToken)
    {
        return Task.FromResult(_urlDictionary.GetOrDefault(logUri, null));
    }

    public Task<string?> GetProjectNameAsync(string id)
    {
        throw new NotImplementedException();
    }
}
