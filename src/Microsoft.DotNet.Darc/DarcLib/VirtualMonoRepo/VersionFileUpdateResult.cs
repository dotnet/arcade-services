// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models.Darc;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Result of the version file update attempt.
/// The merge can fail if there are unresolvable conflicts (in other files than version files for instance).
/// </summary>
/// <param name="ConflictedFiles">Lis of conflicts (if any) preventing from merging the branches</param>
/// <param name="DependencyUpdates">List of dependencies updated during the process</param>
public record VersionFileUpdateResult(
    IReadOnlyCollection<UnixPath> ConflictedFiles,
    List<DependencyUpdate> DependencyUpdates);
