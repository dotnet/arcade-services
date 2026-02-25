// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class BuildReferenceIdentifier
{
    public BuildReferenceIdentifier(
        string org,
        string project,
        int buildId,
        string buildUrl,
        int definitionId,
        string definitionName,
        string repositoryId,
        string sourceSha,
        string targetBranch,
        bool isCompleted = false)
    {
        Org = org;
        Project = project;
        BuildId = buildId;
        BuildUrl = buildUrl;
        DefinitionId = definitionId;
        DefinitionName = definitionName;
        RepositoryId = repositoryId;
        SourceSha = sourceSha;
        TargetBranch = targetBranch;
        IsCompleted = isCompleted;
    }

    public string Org { get; }

    /// <summary>
    /// Azure DevOps project name (e.g. "public", "internal")
    /// </summary>
    public string Project { get; }

    /// <summary>
    /// Azure DevOps build ID (numerical value)
    /// </summary>
    public int BuildId { get; }

    /// <summary>
    /// Azure DevOps build Url
    /// </summary>
    public string BuildUrl { get; }

    /// <summary>
    /// Azure DevOps Pipeline ID
    /// </summary>
    public int DefinitionId { get; }

    /// <summary>
    /// Azure DevOps Pipeline Name
    /// </summary>
    public string DefinitionName { get; }

    /// <summary>
    /// Name of the repository from the system that it originated from. (e.g. dotnet/arcade from GitHub)
    /// </summary>
    public string RepositoryId { get; }


    /// <summary>
    /// Name of the branch for which the PR was originally open
    /// </summary>
    public string TargetBranch { get; }

    /// <summary>
    /// Value of the SHA that was the source of the code used in the build
    /// </summary>
    public string SourceSha { get; }

    /// <summary>
    /// Is the build completed
    /// </summary>
    public bool IsCompleted { get; }

    public override bool Equals(object? obj)
    {
        if (obj == null) return false;
        if (obj.GetType() != typeof(BuildReferenceIdentifier)) return false;

        var input = (BuildReferenceIdentifier)obj;

        if (Org == input.Org &&
            Project == input.Project &&
            BuildId == input.BuildId &&
            RepositoryId == input.RepositoryId &&
            SourceSha == input.SourceSha)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Org);
        hash.Add(Project);
        hash.Add(BuildId);
        hash.Add(RepositoryId);
        hash.Add(SourceSha);
        return hash.ToHashCode();
    }
}
