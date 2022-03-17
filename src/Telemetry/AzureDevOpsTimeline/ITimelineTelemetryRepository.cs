// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.AzureDevOps.Authentication;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public interface ITimelineTelemetryRepository : IDisposable
    {
        public Task<DateTimeOffset?> GetLatestTimelineBuild(string project);
        public Task WriteTimelineBuilds(IEnumerable<AugmentedBuild> augmentedBuilds);
        public Task WriteTimelineValidationMessages(IEnumerable<(int buildId, BuildRequestValidationResult validationResult)> validationResults);
        public Task WriteTimelineRecords(IEnumerable<AugmentedTimelineRecord> records);
        public Task WriteTimelineIssues(IEnumerable<AugmentedTimelineIssue> issues);
    }
}
