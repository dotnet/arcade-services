// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.DarcLib.Models.Darc;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

public static class DependencyExtensions
{
    public static DependencyDetail? GetArcadeUpdate(this IEnumerable<DependencyDetail> updates)
        => updates.FirstOrDefault(i => string.Equals(i.Name, DependencyFileManager.ArcadeSdkPackageName, StringComparison.OrdinalIgnoreCase));

    public static DependencyDetail? GetArcadeUpdate(this IEnumerable<DependencyUpdate> updates)
        => updates.Select(u => u.To).GetArcadeUpdate();
}
