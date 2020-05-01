using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Maestro.ScenarioTests.ObjectHelpers
{
    public class BuildRefComparer : IEqualityComparer<BuildRef>, IComparer
    {
        public int Compare(BuildRef x, BuildRef y)
        {
            return x.BuildId.CompareTo(y.BuildId);
        }

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
}
