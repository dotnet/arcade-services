// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.KnownIssues.Models;

public class KnownIssuesHits
{
    public int Daily { get; }
    public int Weekly { get; }
    public int Monthly { get; }

    public KnownIssuesHits(int daily, int weekly, int monthly)
    {
        Daily = daily;
        Weekly = weekly;
        Monthly = monthly;
    }
}
