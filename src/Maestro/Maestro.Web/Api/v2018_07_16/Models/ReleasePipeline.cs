// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using JetBrains.Annotations;

namespace Maestro.Web.Api.v2018_07_16.Models
{
    public class ReleasePipeline
    {
        public ReleasePipeline([NotNull] Data.Models.ReleasePipeline other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            Id = other.Id;
            PipelineIdentifier = other.PipelineIdentifier;
            Organization = other.Organization;
            Project = other.Project;
        }

        public int Id { get; set; }

        public int PipelineIdentifier { get; set; }

        public string Organization { get; set; }

        public string Project { get; set; }
    }
}
