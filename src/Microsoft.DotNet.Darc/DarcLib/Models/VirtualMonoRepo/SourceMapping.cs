// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

/// <summary>
/// Model for the configuration JSON file with list of individual repositories.
/// </summary>
public record SourceMapping(
    string Name,
    string DefaultRemote,
    string DefaultRef,
    IReadOnlyCollection<string> Include,
    IReadOnlyCollection<string> Exclude,
    string? Version = null);
