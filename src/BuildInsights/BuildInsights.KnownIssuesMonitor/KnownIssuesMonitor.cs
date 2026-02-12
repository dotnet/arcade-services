// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.KnownIssues;

namespace BuildInsights.KnownIssuesMonitor;

public class KnownIssuesMonitor
{
    private readonly IKnownIssueReporter _issueReporter;

    public KnownIssuesMonitor(IKnownIssueReporter issueReporter)
    {
        _issueReporter = issueReporter;
    }

    // TODO: This needs to be a container job
    [CronSchedule("0 0 * ? * * *", TimeZones.PST)] //Every day every hour
    public async Task ExecuteKnownIssueReporter(CancellationToken cancellationToken)
    {
        await _issueReporter.ExecuteKnownIssueReporter();
    }

    public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(TimeSpan.MaxValue);
    }
}
