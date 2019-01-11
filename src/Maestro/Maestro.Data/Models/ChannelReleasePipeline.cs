// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Maestro.Data.Models
{
    public class ChannelReleasePipeline
    {
        public int ChannelId { get; set; }

        public Channel Channel { get; set; }

        public int ReleasePipelineId { get; set; }

        public ReleasePipeline ReleasePipeline { get; set; }
    }
}
