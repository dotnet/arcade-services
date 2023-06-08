// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Represents related metadata needed to update a repository in the VMR.
/// </summary>
/// <param name="Mapping">Source mapping that is being updated</param>
/// <param name="RemoteUri">Remote URI from which we will pull the new updates</param>
/// <param name="TargetRevision">Target revision (usually commit SHA but also a branch or a tag) to update to</param>
/// <param name="TargetVersion">Version of packages built for that given SHA (e.g. 8.0.0-alpha.1.22614.1)</param>
/// <param name="Parent">Parent dependency in the dependency tree that caused this update,null for root (installer)</param>
public record VmrDependencyUpdate(
    SourceMapping Mapping,
    string RemoteUri,
    string TargetRevision,
    string? TargetVersion,
    SourceMapping? Parent);
