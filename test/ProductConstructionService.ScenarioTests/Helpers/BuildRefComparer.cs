// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.ScenarioTests.Helpers;

public class BuildRefComparer : IEqualityComparer<BuildRef>, IComparer
{
    public int Compare(object x, object y)
    {
        return ((BuildRef)x).BuildId.CompareTo(((BuildRef)y).BuildId);
    }

    public bool Equals(BuildRef x, BuildRef y)
    {
        return x.BuildId == y.BuildId &&
            x.IsProduct == y.IsProduct &&
            x.TimeToInclusionInMinutes == y.TimeToInclusionInMinutes;
    }

    public int GetHashCode(BuildRef obj)
    {
        return HashCode.Combine(obj.BuildId, obj.IsProduct, obj.TimeToInclusionInMinutes);
    }
}
