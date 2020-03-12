// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.Serialization;

namespace ReleasePipelineRunner
{
    [DataContract]
    public sealed class ReleasePipelineRunnerItem
    {
        [DataMember]
        public int BuildId { get; set; }

        [DataMember]
        public int ChannelId { get; set; }

        [DataMember]
        public int NumberOfRetriesMade { get; set; }
    }
}
