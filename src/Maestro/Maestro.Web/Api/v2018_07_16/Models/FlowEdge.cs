// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Fabric.Description;
using System.Linq;
using Maestro.Web.Api.v2019_01_16.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.DotNet.DarcLib;
using Newtonsoft.Json;

namespace Maestro.Web.Api.v2018_07_16.Models
{
    public class FlowEdge
    {
        public FlowEdge (string to, string from)
        {
            ToId = to;
            FromId = from;
        }

        public FlowEdge (
            string to,
            string from,
            Guid subscriptionId,
            bool onLongestBuildPath,
            bool isToolingOnly,
            bool? partOfCycle,
            bool isBackEdge)
        {
            ToId = to;
            FromId = from;
            SubscriptionId = subscriptionId;
            OnLongestBuildPath = onLongestBuildPath;
            IsToolingOnly = isToolingOnly;
            PartOfCycle = partOfCycle;
            IsBackEdge = isBackEdge;
        }

        public static FlowEdge Create(DependencyFlowEdge other)
        {
            return new FlowEdge(
                other.To.Id, 
                other.From.Id,
                other.Subscription.Id,
                other.OnLongestBuildPath, 
                other.IsToolingOnly,
                other.PartOfCycle,
                other.BackEdge);
        }

        public string ToId { get; }
        public string FromId { get; }
        public Guid SubscriptionId { get; }
        public bool OnLongestBuildPath { get; }
        public bool IsToolingOnly { get; }
        public bool? PartOfCycle { get; }

        [JsonIgnore]
        public bool IsBackEdge { get; set; }
    }
}
