// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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

        [Required]
        public IDictionary<int, Build> Builds { get; }
    }
}
