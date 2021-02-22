// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

namespace DotNet.Status.Web.Models
{
    public class GrafanaNotificationMatch
    {
        public string Metric { get; set; }
        public ImmutableDictionary<string, string> Tags { get; set; }
        public double Value { get; set; }
    }
}
