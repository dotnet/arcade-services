// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;

public record VersionFileChanges<T>(
    List<string> Removals,
    Dictionary<string, T> Additions,
    Dictionary<string, T> Updates) 
    where T : IVersionFileProperty
{
}
