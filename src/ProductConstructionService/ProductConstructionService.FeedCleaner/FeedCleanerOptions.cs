// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.FeedCleaner;

public class FeedCleanerOptions
{
    public required bool Enabled { get; set; }

    public required List<ReleasePackageFeed> ReleasePackageFeeds { get; set; }

    public IEnumerable<string> GetAzdoAccounts()
        => ReleasePackageFeeds
            .Select(feed => feed.Account)
            .Distinct();
}

public record ReleasePackageFeed(string Account, string Project, string Name);
