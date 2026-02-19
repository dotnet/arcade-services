// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Model;

public record CodeflowSubscription(
    string RepositoryUrl,
    string? RepositoryBranch,
    string MappingName,
    bool Enabled,
    Subscription? BackflowSubscription,
    Subscription? ForwardflowSubscription,
    string? BackflowPr,
    string? ForwardflowPr);

public record CodeflowPage(
    Build VmrBuild,
    List<CodeflowSubscriptionPageEntry> CodeflowRow);

public record CodeflowSubscriptionPageEntry(
    string RepositoryUrl,
    string MappingName,
    bool Enabled,
    SubscriptionEntry? ForwardFlowSubscription,
    SubscriptionEntry? BackflowSubscription);

public record SubscriptionEntry(
    Subscription Subscription,
    int lastAppliedBuildDistanceDays,
    ActivePr? ActivePr);

public record ActivePr(
    DateTime CreatedDate,
    string Url);

