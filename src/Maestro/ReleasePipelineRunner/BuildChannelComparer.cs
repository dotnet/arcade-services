// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data.Models;
using System.Collections.Generic;

namespace ReleasePipelineRunner
{
    sealed public class BuildChannelComparer : IEqualityComparer<BuildChannel>
    {
        public bool Equals(BuildChannel x, BuildChannel y)
        {
            return x.BuildId == y.BuildId && x.ChannelId == y.ChannelId;
        }

        public int GetHashCode(BuildChannel obj)
        {
            return obj.ChannelId * 31 + obj.BuildId;
        }
    }
}
