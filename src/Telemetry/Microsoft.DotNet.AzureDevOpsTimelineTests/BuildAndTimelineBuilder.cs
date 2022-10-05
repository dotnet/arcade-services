// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;
using System;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests;

public class BuildAndTimelineBuilder
{
    private BuildAndTimeline _build;

    public TimelineBuilder AddTimeline(string id, DateTimeOffset lastChangedOn)
    {
        return new TimelineBuilder(id, lastChangedOn);
    }

    public BuildAndTimelineBuilder AddTimeline(TimelineBuilder timelineBuilder)
    {
        _build.Timelines.Add(timelineBuilder.Build());

        return this;
    }

    public BuildAndTimelineBuilder(Build build)
    {
        _build = new BuildAndTimeline(build);
    }

    public static BuildAndTimelineBuilder NewPullRequestBuild(int id, string projectName, string branchName)
    {
        Build build = new Build()
        {
            Id = id,
            Project = new TeamProjectReference() { Name = projectName },
            ValidationResults = Array.Empty<BuildRequestValidationResult>(),
            Reason = "pullRequest"
        };

        if (branchName == null)
        {
            build.Parameters = null;
        }
        else
        {
            build.Parameters = $"{{\"system.pullRequest.targetBranch\": \"{branchName}\"}}";
        }

        return new BuildAndTimelineBuilder(build);
    }

    public BuildAndTimeline Build()
    {
        return _build;
    }
}
