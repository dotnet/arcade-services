// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace Microsoft.DotNet.DarcLib.Models.Darc;

/// <summary>
///     Represents a dependency update, from an existing
///     dependency detail to a new dependency detail
/// </summary>
[DebuggerDisplay("{From} -> {To}")]
public class DependencyUpdate
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
}
