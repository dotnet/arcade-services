// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

namespace DotNet.Status.Web.Controllers
{
    public class GrafanaNotification
    {
        public GrafanaNotification(
            string title,
            int ruleId,
            string ruleName,
            string ruleUrl,
            string state,
            string imageUrl,
            string message,
            IImmutableList<GrafanaNotificationMatch> evalMatches,
            ImmutableDictionary<string, string> tags)
        {
            Title = title;
            RuleId = ruleId;
            RuleName = ruleName;
            RuleUrl = ruleUrl;
            State = state;
            ImageUrl = imageUrl;
            Message = message;
            EvalMatches = evalMatches;
            Tags = tags;
        }

        public string Title { get; }
        public int RuleId { get; }
        public string RuleName { get; }
        public string RuleUrl { get; }
        public string State { get; }
        public string ImageUrl { get; }
        public string Message { get; }
        public IImmutableList<GrafanaNotificationMatch> EvalMatches { get; }
        public ImmutableDictionary<string, string> Tags { get; }
    }
}
