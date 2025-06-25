// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Represents related metadata needed to update a repository in the VMR.
/// </summary>
/// <param name="Mapping">Source mapping that is being updated</param>
/// <param name="RemoteUri">Remote URI from which we will pull the new updates</param>
/// <param name="TargetRevision">Target revision (usually commit SHA but also a branch or a tag) to update to</param>
/// <param name="Parent">Parent dependency in the dependency tree that caused this update,null for root (installer)</param>                                                          
/// <param name="OfficialBuildId">Id of the build that triggered the codeflow. Empty when flowing non bar builds/code</param>
/// <param name="BarId">Bar Id of the build that triggered the codeflow. Empty when flowing non bar builds/code</param>
public record VmrDependencyUpdate(
    SourceMapping Mapping,
    string RemoteUri,
    string TargetRevision,
    SourceMapping? Parent,
    string? OfficialBuildId,
    int? BarId,
    string? OriginRevision = null);
