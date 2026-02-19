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
            channelName: ".NET 10 Dev",
            enabled: true,
            lastAppliedBuildDaysAgo: 1);

        var runtimeSubscriptionBf = CreateSubscription(
            sourceRepository: "https://github.com/dotnet/dotnet",
            targetRepository: "https://github.com/dotnet/runtime",
            targetBranch: "main",
            sourceDirectory: "src/runtime",
            targetDirectory: null,
            channelName: ".NET 10 Dev",
            enabled: true,
            lastAppliedBuildDaysAgo: 2);

        var sdkSubscriptionFf = CreateSubscription(
            sourceRepository: "https://github.com/dotnet/sdk",
            targetRepository: "https://github.com/dotnet/dotnet",
            targetBranch: "main",
            sourceDirectory: null,
            targetDirectory: "src/sdk",
            channelName: ".NET 10 Dev",
            enabled: true,
            lastAppliedBuildDaysAgo: 0);

        var sdkSubscriptionBf = CreateSubscription(
            sourceRepository: "https://github.com/dotnet/dotnet",
            targetRepository: "https://github.com/dotnet/sdk",
            targetBranch: "main",
            sourceDirectory: "src/sdk",
            targetDirectory: null,
            channelName: ".NET 10 Dev",
            enabled: true,
            lastAppliedBuildDaysAgo: 3);

        var aspnetSubscriptionFf = CreateSubscription(
            sourceRepository: "https://github.com/dotnet/aspnetcore",
            targetRepository: "https://github.com/dotnet/dotnet",
            targetBranch: "main",
            sourceDirectory: null,
            targetDirectory: "src/aspnetcore",
            channelName: ".NET 10 Dev",
            enabled: false,
            lastAppliedBuildDaysAgo: 5);

        var newestRuntimeBuild = CreateNewestBuild("https://github.com/dotnet/runtime", daysAgo: 0);
        var newestSdkBuild = CreateNewestBuild("https://github.com/dotnet/sdk", daysAgo: 0);
        var newestAspnetBuild = CreateNewestBuild("https://github.com/dotnet/aspnetcore", daysAgo: 0);

        var entries = new List<CodeflowSubscriptionPageEntry>
        {
            new(
                RepositoryUrl: "https://github.com/dotnet/runtime",
                MappingName: "runtime",
                Enabled: true,
                ForwardFlowSubscription: new SubscriptionEntry(
                    runtimeSubscriptionFf,
                    LastAppliedBuildStaleness: 1,
                    NewestApplicableBuild: newestRuntimeBuild,
                    ActivePr: new ActivePr(
                        CreatedDate: new DateTime(2026, 2, 18, 14, 0, 0),
                        Url: "https://github.com/dotnet/runtime/pull/112345")),
                BackflowSubscription: new SubscriptionEntry(
                    runtimeSubscriptionBf,
                    LastAppliedBuildStaleness: 2,
                    NewestApplicableBuild: vmrBuild,
                    ActivePr: null)),

            new(
                RepositoryUrl: "https://github.com/dotnet/sdk",
                MappingName: "sdk",
                Enabled: true,
                ForwardFlowSubscription: new SubscriptionEntry(
                    sdkSubscriptionFf,
                    LastAppliedBuildStaleness: 1,
                    NewestApplicableBuild: newestSdkBuild,
                    ActivePr: null),
                BackflowSubscription: new SubscriptionEntry(
                    sdkSubscriptionBf,
                    LastAppliedBuildStaleness: 3,
                    NewestApplicableBuild: vmrBuild,
                    ActivePr: new ActivePr(
                        CreatedDate: new DateTime(2026, 2, 17, 9, 15, 0),
                        Url: "https://github.com/dotnet/dotnet/pull/54321"))),

            new(
                RepositoryUrl: "https://github.com/dotnet/aspnetcore",
                MappingName: "aspnetcore",
                Enabled: false,
                ForwardFlowSubscription: new SubscriptionEntry(
                    aspnetSubscriptionFf,
                    LastAppliedBuildStaleness: 5,
                    NewestApplicableBuild: newestAspnetBuild,
                    ActivePr: null),
                BackflowSubscription: null),
        };

        return new CodeflowPage(entries);
    }

    private static Subscription CreateSubscription(
        string sourceRepository,
        string targetRepository,
        string targetBranch,
        string? sourceDirectory,
        string? targetDirectory,
        string channelName,
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
            Channel = new Channel(id: Random.Shared.Next(100, 999), channelName, "none"),
            LastAppliedBuild = new Build(
                id: Random.Shared.Next(10000, 99999),
                dateProduced: DateTimeOffset.UtcNow.AddMinutes(-Random.Shared.Next(1, 14 * 24 * 60)),
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

    private static Build CreateNewestBuild(string repository, int daysAgo)
    {
        return new Build(
            id: Random.Shared.Next(10000, 99999),
            dateProduced: DateTimeOffset.UtcNow.AddDays(-daysAgo),
            staleness: 0,
            released: false,
            stable: true,
            commit: Guid.NewGuid().ToString("N")[..16],
            channels: [],
            assets: [],
            dependencies: [],
            incoherencies: [])
        {
            GitHubRepository = repository,
            GitHubBranch = "main",
        };
    }
}
