// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

//https://learn.microsoft.com/en-us/dotnet/api/microsoft.teamfoundation.build.webapi.repositorytypes?view=azure-devops-dotnet
public enum BuildRepositoryType
{
    Unknown,
    Git,
    GitHub,
    TfsGit, //Team Foundation Server Git
    TfsVersionControl
}
