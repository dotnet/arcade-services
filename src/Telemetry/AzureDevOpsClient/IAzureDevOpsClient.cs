// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.Internal.AzureDevOps;

public interface IAzureDevOpsClient
{
    public Task<Build[]> ListBuilds(string project, CancellationToken cancellationToken, DateTimeOffset? minTime = default, int? limit = default);
    public Task<AzureDevOpsProject[]?> ListProjectsAsync(CancellationToken cancellationToken = default);
    public Task<Build?> GetBuildAsync(string project, long buildId, CancellationToken cancellationToken = default);
    public Task<(BuildChange[]? changes, int? truncatedChangeCount)?> GetBuildChangesAsync(string project, long buildId, CancellationToken cancellationToken = default);
    public Task<Timeline?> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken);
    public Task<Timeline?> GetTimelineAsync(string project, int buildId, string timelineId, CancellationToken cancellationToken);
    public Task<BuildChangeDetail?> GetChangeDetails(string changeUrl, CancellationToken cancellationToken = default);
    public Task<WorkItem?> CreateRcaWorkItem(string project, string title, CancellationToken cancellationToken = default);
    public Task<string?> MatchLogLineSequence(string logUri, IReadOnlyList<Regex> regexes, CancellationToken cancellationToken = default);
    public Task<string?> GetProjectNameAsync(string id);
}
