using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Maestro.Web.Api.v2020_02_20.Models
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

        [Required]
        public IDictionary<int, Build> Builds { get; }
    }
}
