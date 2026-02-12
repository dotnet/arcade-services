// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace BuildInsights.BuildAnalysis.Models;

public class RelatedBuilds
{
    public RelatedBuilds(ImmutableList<BuildReferenceIdentifier> relatedBuildsList)
    {
        RelatedBuildsList = relatedBuildsList;
    }

    public RelatedBuilds(IEnumerable<BuildReferenceIdentifier> relatedBuilds) : this(relatedBuilds.ToImmutableList())
    {
    }

    public ImmutableList<BuildReferenceIdentifier> RelatedBuildsList { get; }
}
