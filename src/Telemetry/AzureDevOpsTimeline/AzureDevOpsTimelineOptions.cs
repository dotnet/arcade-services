// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public class AzureDevOpsTimelineOptions
    {
        public string KustoQueryConnectionString { get; set; }
        public string KustoIngestConnectionString { get; set; }
        public string KustoDatabase { get; set; }

        public string AzureDevOpsAccessToken { get; set; }
        public string AzureDevOpsProjects { get; set; }
        public string AzureDevOpsOrganization { get; set; }
        public string AzureDevOpsUrl { get; set; }

        public string InitialDelay { get; set; }
        public string Interval { get; set; }
        public string ParallelRequests { get; set; }
    }
}
