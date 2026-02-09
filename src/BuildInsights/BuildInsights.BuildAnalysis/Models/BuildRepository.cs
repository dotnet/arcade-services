// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class BuildRepository
{
    public string Name { get; }
    public BuildRepositoryType Type { get; }

    public BuildRepository(string name = null, string type = null)
    {
        Name = name;
        Type = MapRepositoryType(type);
    }

    public BuildRepositoryType MapRepositoryType(string repositoryType)
    {
        return repositoryType switch
        {
            "Git" => BuildRepositoryType.Git,
            "GitHub" => BuildRepositoryType.GitHub,
            "TfsGit" => BuildRepositoryType.TfsGit,
            "TfsVersionControl" => BuildRepositoryType.TfsVersionControl,
            _ => BuildRepositoryType.Unknown
        };
    }
}
