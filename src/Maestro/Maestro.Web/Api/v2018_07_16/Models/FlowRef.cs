// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Maestro.Web.Api.v2019_01_16.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Newtonsoft.Json;

namespace Maestro.Web.Api.v2018_07_16.Models
{
    public class FlowRef
    {
        [JsonConstructor]
        public FlowRef(int defaultChannelId, string repository, string branch)
        {
            DefaultChannelId = defaultChannelId;
            Repository = repository;
            Branch = branch;
        }

        public FlowRef(int defaultChannelId, string repository, string branch, double officialBuildTime, double prBuildTime)
        {
            DefaultChannelId = defaultChannelId;
            Repository = repository;
            Branch = branch;
            OfficialBuildTime = officialBuildTime;
            PRBuildTime = prBuildTime;
        }

        public int DefaultChannelId { get; }
        public string Repository { get; }
        public string Branch { get; }
        public double OfficialBuildTime { get; }
        public double PRBuildTime { get; }
    }

    public class FlowEdge
    {
        public FlowEdge (int to, int from)
        {
            ToId = to;
            FromId = from;
        }

        public int ToId { get; }
        public int FromId { get; }
    }
}
