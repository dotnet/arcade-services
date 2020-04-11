// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Maestro.Web.Api.v2020_02_20.Models
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

    public class BuildIncoherence : IValidatableObject
    {
        public List<IncoherentDependency> IncoherentDeps { get; set; }

        public List<IncoherentNode> IncoherentNodes { get; set; }

        public BuildIncoherence(Data.Models.BuildIncoherence other)
        {
            if (other == null)
            {
                return;
            }

            IncoherentDeps = new List<IncoherentDependency>();
            IncoherentNodes = new List<IncoherentNode>();

            foreach (var item in other.IncoherentDeps)
            {
                IncoherentDeps.Add(new IncoherentDependency
                {
                    Name = item.Name,
                    Version = item.Version,
                    Commit = item.Commit,
                    Repository = item.Repository
                });
            }

            foreach (var item in other.IncoherentNodes)
            {
                IncoherentNodes.Add(new IncoherentNode
                {
                    Repository = item.Repository,
                    Commit = item.Commit
                });
            }
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            return new List<ValidationResult>();
        }

        public Data.Models.BuildIncoherence ToDb()
        {
            var deps = new List<Data.Models.IncoherentDependency>();
            var nodes = new List<Data.Models.IncoherentNode>();

            foreach (var item in IncoherentDeps)
            {
                deps.Add(new Data.Models.IncoherentDependency
                {
                    Name = item.Name,
                    Version = item.Version,
                    Repository = item.Repository,
                    Commit = item.Commit
                });
            }

            foreach (var item in IncoherentNodes)
            {
                nodes.Add(new Data.Models.IncoherentNode
                {
                    Repository = item.Repository,
                    Commit = item.Commit
                });
            }

            return new Data.Models.BuildIncoherence(deps, nodes);
        }
    }
}
