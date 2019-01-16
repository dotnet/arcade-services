// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Maestro.Data.Models
{
    public class ReleasePipeline
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public string Organization { get; set; }

        public string Project { get; set; }

        public int PipelineIdentifier { get; set; }

        public List<ChannelReleasePipeline> ChannelReleasePipelines { get; set; }
    }
}
