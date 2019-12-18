// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;

namespace DotNet.Status.Web
{
    public class GitHubConnectionOptions
    {
        public string Organization { get; set; }
        public string Repository { get; set; }
        public string NotificationTarget { get; set; }
        public ImmutableArray<string> AlertLabels { get; set; }
        public string TitlePrefix { get; set; }
        public string SupplementalBodyText { get; set; }
    }
}
