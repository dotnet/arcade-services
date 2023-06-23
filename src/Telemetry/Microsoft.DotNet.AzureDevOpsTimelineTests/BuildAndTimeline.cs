// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;
using System.Collections.Generic;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests;

public class BuildAndTimeline
{
    public Build Build { get; }
    public IList<Timeline> Timelines { get; } = new List<Timeline>();

    public BuildAndTimeline(Build build)
    {
        Build = build;
    }

    public BuildAndTimeline(Build build, IList<Timeline> timelines)
    {
        Build = build;
        Timelines = timelines;
    }
}
