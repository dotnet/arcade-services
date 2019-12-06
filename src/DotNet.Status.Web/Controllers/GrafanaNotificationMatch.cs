// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

namespace DotNet.Status.Web.Controllers
{
    public class GrafanaNotificationMatch
    {
        public GrafanaNotificationMatch(string metric, ImmutableDictionary<string, string> tags, double value)
        {
            Metric = metric;
            Tags = tags;
            Value = value;
        }

        public string Metric { get; }
        public ImmutableDictionary<string, string> Tags { get; }
        public double Value { get; }
    }
}
