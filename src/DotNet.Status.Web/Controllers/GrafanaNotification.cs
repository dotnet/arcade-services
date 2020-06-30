// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

namespace DotNet.Status.Web.Controllers
{
    public class GrafanaNotification
    {
        public string Title { get; set; }
        public int RuleId { get; set; }
        public string RuleName { get; set; }
        public string RuleUrl { get; set; }
        public string State { get; set; }
        public string ImageUrl { get; set; }
        public string Message { get; set; }
        public IImmutableList<GrafanaNotificationMatch> EvalMatches { get; set; }
        public ImmutableDictionary<string, string> Tags { get; set; }
    }
}
