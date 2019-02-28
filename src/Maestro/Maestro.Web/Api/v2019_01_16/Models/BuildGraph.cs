using System.Collections.Generic;
using System.Linq;

namespace Maestro.Web.Api.v2019_01_16.Models
{
    public class BuildGraph
    {
        public static BuildGraph Create(IEnumerable<Build> builds)
        {
            return new BuildGraph(builds.ToDictionary(b => b.Id, b => b));
        }

        public BuildGraph(IDictionary<int, Build> builds)
        {
            Builds = builds;
        }

        public IDictionary<int, Build> Builds { get; }
    }
}
