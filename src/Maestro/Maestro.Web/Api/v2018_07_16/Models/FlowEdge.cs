// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.DotNet.DarcLib;

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
            bool backEdge)
        {
            ToId = to;
            FromId = from;
            SubscriptionId = subscriptionId;
            OnLongestBuildPath = onLongestBuildPath;
            IsToolingOnly = isToolingOnly;
            PartOfCycle = partOfCycle;
            BackEdge = backEdge;
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
        public bool BackEdge { get; set; }
    }
}
