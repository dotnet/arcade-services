// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.Serialization;

namespace ReleasePipelineRunner
{
    [DataContract]
    public sealed class ReleasePipelineStatusItem
    {
        [DataMember]
        public int ReleaseId { get; set; }

        [DataMember]
        public int ChannelId { get; set; }

        [DataMember]
        public string PipelineOrganization { get; set; }

        [DataMember]
        public string PipelineProject { get; set; }

        public ReleasePipelineStatusItem()
        {

        }
        public ReleasePipelineStatusItem(int releaseId, int channelId, string pipelineOrganization, string pipelineProject)
        {
            ReleaseId = releaseId;
            ChannelId = channelId;
            PipelineOrganization = pipelineOrganization;
            PipelineProject = pipelineProject;
        }
    }
}
