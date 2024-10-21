// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Linq;

namespace FeedCleanerService;

public class FeedCleanerOptions
{
    public bool Enabled { get; set; }

    public List<(string account, string project, string name)> ReleasePackageFeeds = [];

    public IEnumerable<string> GetAzdoAccounts()
        => ReleasePackageFeeds
            .Select(feed => feed.account)
            .Distinct();
}
