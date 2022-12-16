// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public record VmrDependencyUpdate(
    SourceMapping Mapping,
    string RemoteUri,
    string TargetRevision,
    string? TargetVersion,
    SourceMapping? Parent);
