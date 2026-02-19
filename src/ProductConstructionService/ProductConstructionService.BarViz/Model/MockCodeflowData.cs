// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ProductConstructionService.Client.Models;

namespace ProductConstructionService.BarViz.Model;

public static class MockCodeflowData
{
    public static CodeflowPage GetMockCodeflowPage()
    {
        var vmrBuild = new Build(
            id: 12345,
            dateProduced: new DateTimeOffset(2026, 2, 18, 10, 30, 0, TimeSpan.Zero),
            staleness: 0,
            released: false,
            stable: true,
            commit: "abc123def456789",
            channels: [],
            assets: [],
            dependencies: [],
            incoherencies: [])
        {
            GitHubRepository = "https://github.com/dotnet/dotnet",
            GitHubBranch = "main",
        };

        var runtimeSubscriptionFf = CreateSubscription(
            sourceRepository: "https://github.com/dotnet/runtime",
            targetRepository: "https://github.com/dotnet/dotnet",
            targetBranch: "main",
            sourceDirectory: null,
            targetDirectory: "src/runtime",
            enabled: true,
            lastAppliedBuildDaysAgo: 1);

        var runtimeSubscriptionBf = CreateSubscription(
            sourceRepository: "https://github.com/dotnet/dotnet",
            targetRepository: "https://github.com/dotnet/runtime",
            targetBranch: "main",
            sourceDirectory: "src/runtime",
            targetDirectory: null,
            enabled: true,
            lastAppliedBuildDaysAgo: 2);

        var sdkSubscriptionFf = CreateSubscription(
            sourceRepository: "https://github.com/dotnet/sdk",
            targetRepository: "https://github.com/dotnet/dotnet",
            targetBranch: "main",
            sourceDirectory: null,
            targetDirectory: "src/sdk",
            enabled: true,
            lastAppliedBuildDaysAgo: 0);

        var sdkSubscriptionBf = CreateSubscription(
            sourceRepository: "https://github.com/dotnet/dotnet",
            targetRepository: "https://github.com/dotnet/sdk",
            targetBranch: "main",
            sourceDirectory: "src/sdk",
            targetDirectory: null,
            enabled: true,
            lastAppliedBuildDaysAgo: 3);

        var aspnetSubscriptionFf = CreateSubscription(
            sourceRepository: "https://github.com/dotnet/aspnetcore",
            targetRepository: "https://github.com/dotnet/dotnet",
            targetBranch: "main",
            sourceDirectory: null,
            targetDirectory: "src/aspnetcore",
            enabled: false,
            lastAppliedBuildDaysAgo: 5);

        var entries = new List<CodeflowSubscriptionPageEntry>
        {
            new(
                RepositoryUrl: "https://github.com/dotnet/runtime",
                MappingName: "runtime",
                Enabled: true,
                ForwardFlowSubscription: new SubscriptionEntry(
                    runtimeSubscriptionFf,
                    lastAppliedBuildDistanceDays: 1,
                    ActivePr: new ActivePr(
                        CreatedDate: new DateTime(2026, 2, 18, 14, 0, 0),
                        Url: "https://github.com/dotnet/runtime/pull/112345")),
                BackflowSubscription: new SubscriptionEntry(
                    runtimeSubscriptionBf,
                    lastAppliedBuildDistanceDays: 2,
                    ActivePr: null)),

            new(
                RepositoryUrl: "https://github.com/dotnet/sdk",
                MappingName: "sdk",
                Enabled: true,
                ForwardFlowSubscription: new SubscriptionEntry(
                    sdkSubscriptionFf,
                    lastAppliedBuildDistanceDays: 0,
                    ActivePr: null),
                BackflowSubscription: new SubscriptionEntry(
                    sdkSubscriptionBf,
                    lastAppliedBuildDistanceDays: 3,
                    ActivePr: new ActivePr(
                        CreatedDate: new DateTime(2026, 2, 17, 9, 15, 0),
                        Url: "https://github.com/dotnet/dotnet/pull/54321"))),

            new(
                RepositoryUrl: "https://github.com/dotnet/aspnetcore",
                MappingName: "aspnetcore",
                Enabled: false,
                ForwardFlowSubscription: new SubscriptionEntry(
                    aspnetSubscriptionFf,
                    lastAppliedBuildDistanceDays: 5,
                    ActivePr: null),
                BackflowSubscription: null),
        };

        return new CodeflowPage(vmrBuild, entries);
    }

    private static Subscription CreateSubscription(
        string sourceRepository,
        string targetRepository,
        string targetBranch,
        string? sourceDirectory,
        string? targetDirectory,
        bool enabled,
        int lastAppliedBuildDaysAgo)
    {
        var subscription = new Subscription(
            id: Guid.NewGuid(),
            enabled: enabled,
            sourceEnabled: true,
            sourceRepository: sourceRepository,
            targetRepository: targetRepository,
            targetBranch: targetBranch,
            sourceDirectory: sourceDirectory ?? string.Empty,
            targetDirectory: targetDirectory ?? string.Empty,
            pullRequestFailureNotificationTags: string.Empty,
            excludedAssets: [])
        {
            LastAppliedBuild = new Build(
                id: Random.Shared.Next(10000, 99999),
                dateProduced: DateTimeOffset.UtcNow.AddDays(-lastAppliedBuildDaysAgo),
                staleness: lastAppliedBuildDaysAgo,
                released: false,
                stable: true,
                commit: Guid.NewGuid().ToString("N")[..16],
                channels: [],
                assets: [],
                dependencies: [],
                incoherencies: [])
            {
                GitHubRepository = sourceRepository,
                GitHubBranch = "main",
            },
        };

        return subscription;
    }
}
