// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Maestro.Web.Api.v2019_01_16.Models
{
    public class BuildTime
    {
        public BuildTime(int defaultChannelId, double officialTime, double prTime)
        {
            DefaultChannelId = defaultChannelId;
            OfficialBuildTime = officialTime;
            PRBuildTime = prTime;
        }

        public int DefaultChannelId;
        public double OfficialBuildTime;
        public double PRBuildTime;
    }
}
