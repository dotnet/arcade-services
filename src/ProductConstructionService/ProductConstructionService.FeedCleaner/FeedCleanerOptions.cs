// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.FeedCleaner;

public class FeedCleanerOptions
{
    public required bool Enabled { get; set; }

    public required List<string> AzdoAccounts { get; set; }
}
