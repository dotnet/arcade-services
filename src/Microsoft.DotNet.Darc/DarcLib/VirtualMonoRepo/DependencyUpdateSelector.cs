// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib.Models.Darc;
using NuGet.Versioning;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Merger for DependencyUpdate objects that compares semantic versions and selects the higher version.
/// </summary>
public class DependencyUpdateSelector : IVersionPropertySelector<DependencyUpdate>
{
    public DependencyUpdate Select(DependencyUpdate repoChange, DependencyUpdate vmrChange)
    {
        if (repoChange.GetType() != typeof(DependencyUpdate) || vmrChange.GetType() != typeof(DependencyUpdate))
        {
            throw new ArgumentException($"Provided updates are not {typeof(DependencyUpdate)}");
        }

        if (SemanticVersion.TryParse(repoChange.To?.Version!, out var repoVersion) &&
            SemanticVersion.TryParse(vmrChange.To?.Version!, out var vmrVersion))
        {
            return repoVersion > vmrVersion ? repoChange : vmrChange;
        }
        
        throw new ArgumentException($"Cannot compare {repoChange.To?.Version} with {vmrChange.To?.Version} because they are not valid semantic versions.");
    }
}
