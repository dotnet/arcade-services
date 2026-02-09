// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface IHelixDataService
{
    bool IsHelixWorkItem(string comment);
    Task<HelixWorkItem> TryGetHelixWorkItem(string workItemInfo, CancellationToken cancellationToken);
    Task<Dictionary<string, List<HelixWorkItem>>> TryGetHelixWorkItems(ImmutableList<string> workItemInfo, CancellationToken cancellationToken);
}
