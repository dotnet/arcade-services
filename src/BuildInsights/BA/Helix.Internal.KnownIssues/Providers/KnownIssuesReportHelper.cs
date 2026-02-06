using Microsoft.Internal.Helix.KnownIssues.Models;
using System.Collections.Immutable;
using System;
using System.Linq;
using Microsoft.Extensions.Internal;

namespace Microsoft.Internal.Helix.KnownIssues.Providers;

public class KnownIssuesReportHelper
{
    private readonly ISystemClock _clock;

    public KnownIssuesReportHelper(ISystemClock clock)
    {
        _clock = clock;
    }

    public KnownIssuesHits GetIssuesHits(ImmutableList<KnownIssueMatch> knownIssueMatches)
    {
        DateTimeOffset now = _clock.UtcNow;
        int daily = knownIssueMatches.Count(k => k.StepStartTime > now.AddHours(-24));
        int weekly = knownIssueMatches.Count(k => k.StepStartTime > now.AddDays(-7));
        int monthly = knownIssueMatches.Count(k => k.StepStartTime > now.AddMonths(-1));

        return new KnownIssuesHits(daily, weekly, monthly);
    }

    public KnownIssuesHits GetIssuesHits(ImmutableList<TestKnownIssueMatch> testKnownIssueMatches)
    {
        DateTimeOffset now = _clock.UtcNow;
        int daily = testKnownIssueMatches.Count(k => k.CompletedDate > now.AddHours(-24));
        int weekly = testKnownIssueMatches.Count(k => k.CompletedDate > now.AddDays(-7));
        int monthly = testKnownIssueMatches.Count(k => k.CompletedDate > now.AddMonths(-1));

        return new KnownIssuesHits(daily, weekly, monthly);
    }
}
