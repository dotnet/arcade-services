// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models;

public record VersionDetails(
    IReadOnlyCollection<DependencyDetail> Dependencies,
    VmrCodeflow? VmrCodeflow);

public record VmrCodeflow(
    string Name,
    Outflow Outflow,
    Inflow Inflow);

public record Outflow(
    IReadOnlyCollection<string> ExcludedFiles);

public record Inflow(
    string Uri,
    string Sha,
    IReadOnlyCollection<string> IgnoredPackages);
