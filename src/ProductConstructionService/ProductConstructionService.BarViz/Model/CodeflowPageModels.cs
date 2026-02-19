// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Model;

public record CodeflowPageData(
    List<CodeflowEntry> Entries);

public record CodeflowEntry(
    string RepositoryUrl,
    string MappingName,
    bool Enabled,
    SubscriptionDetail? ForwardFlowSubscription,
    SubscriptionDetail? BackflowSubscription);

public record SubscriptionDetail(
    Subscription Subscription,
    int LastAppliedBuildStaleness,
    Build? NewestApplicableBuild,
    ActivePullRequest? ActivePullRequest);

public record ActivePullRequest(
    DateTime CreatedDate,
    string Url);
