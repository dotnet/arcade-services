// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Maestro.Client.Models;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib;

public class BuildComparer : IEqualityComparer<Build>
{
    public bool Equals(Build x, Build y)
    {
        return x.Id == y.Id;
    }

    public int GetHashCode(Build obj)
    {
        return obj.Id;
    }
}
