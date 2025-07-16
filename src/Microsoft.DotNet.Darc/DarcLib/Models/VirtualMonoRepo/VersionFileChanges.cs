// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
public record VersionFileChanges(
    List<string> Removals,
    Dictionary<string, IVersionFileProperty> Additions,
    Dictionary<string, IVersionFileProperty> Updates)
{
}
