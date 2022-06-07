// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

public record SourceMapping (
    string Name,
    string DefaultRemote,
    IReadOnlyCollection<string> Include,
    IReadOnlyCollection<string> Exclude,
    string? Version = null,
    string? DefaultBranch = null);
