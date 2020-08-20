using System;
using System.Collections.Generic;
using System.Text;

namespace Maestro.Data.Models
{
    public class DependencyMap
    {
        public DependencyMap()
        {
            DependencyShaMap = new Dictionary<(string from, string to), int>();
        }
        public Dictionary<(string from, string to), int> DependencyShaMap { get; set; }
    }
}
