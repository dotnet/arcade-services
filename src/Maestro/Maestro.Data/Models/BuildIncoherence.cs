// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Maestro.Data.Models
{
    public class IncoherentDependency
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string Repository { get; set; }
        public string Commit { get; set; }
    }

    public class IncoherentNode
    {
        public string Repository { get; set; }
        public string Commit { get; set; }
    }

    public class BuildIncoherence
    {
        public List<IncoherentDependency> IncoherentDeps { get; }
        public List<IncoherentNode> IncoherentNodes { get; }

        public BuildIncoherence(List<IncoherentDependency> incoherentDeps, List<IncoherentNode> incoherentNodes)
        {
            IncoherentDeps = incoherentDeps;
            IncoherentNodes = incoherentNodes;
        }
    }
}
