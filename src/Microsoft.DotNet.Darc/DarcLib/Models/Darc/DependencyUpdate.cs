// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using NuGet.Versioning;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

/// <summary>
///     Represents a dependency update, from an existing
///     dependency detail to a new dependency detail
/// </summary>
[DebuggerDisplay("{From} -> {To}")]
public class DependencyUpdate : VersionFileProperty
{
    /// <summary>
    ///     Current dependency
    /// </summary>
    public DependencyDetail From { get; set; }

    /// <summary>
    ///     Updated dependency
    /// </summary>
    public DependencyDetail To { get; set; }

    public string DependencyName => From?.Name ?? To?.Name;

    public override string GetName() => DependencyName;
    public override bool IsAdded() => From == null;
    public override bool IsGreater(VersionFileProperty otherProperty)
    {
        if (SemanticVersion.TryParse(To?.Version, out var toVersion) &&
            SemanticVersion.TryParse(((DependencyUpdate)otherProperty).To?.Version, out var otherToVersion))
        {
            return toVersion > otherToVersion;
        }
        return false;
    }
    public override bool IsRemoved() => To == null;
    public override bool IsUpdated() => From != null && To != null;
}
