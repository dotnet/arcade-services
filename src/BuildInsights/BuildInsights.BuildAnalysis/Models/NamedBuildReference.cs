// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis.Services;

public class NamedBuildReference : BuildReferenceIdentifier
{
    public string Name { get; }
    public string WebUrl { get; }

    public NamedBuildReference(
        string name,
        string webUrl,
        string org,
        string project,
        int buildId,
        string buildUrl,
        int definitionId,
        string definitionName,
        string repositoryId,
        string sourceSha,
        string targetBranch,
        bool isCompleted = false) : base(org, project, buildId, buildUrl, definitionId, definitionName, repositoryId, sourceSha, targetBranch, isCompleted)
    {
        Name = name;
        WebUrl = webUrl;
    }
}
