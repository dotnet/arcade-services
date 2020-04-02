using System.Collections.Generic;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Maestro.ScenarioTests.ObjectHelpers
{
    public class BuildRefComprarer : IEqualityComparer<BuildRef>
    {
        public bool Equals(BuildRef x, BuildRef y)
        {
            return x.BuildId == y.BuildId &&
                x.IsProduct == y.IsProduct &&
                x.TimeToInclusionInMinutes == y.TimeToInclusionInMinutes;
        }

        public int GetHashCode(BuildRef obj)
        {
            return obj.GetHashCode();
        }
    }
}
