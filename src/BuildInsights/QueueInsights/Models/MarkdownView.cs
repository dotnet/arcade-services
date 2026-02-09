// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;

namespace QueueInsights.Models;

public class MarkdownView
{
    public MarkdownView(IImmutableList<HighestWaitTimeQueueInfoView> longestQueues,
        string repo,
        string commitHash,
        string pullRequest,
        IImmutableList<string> queues,
        IImmutableList<string> onPremQueues,
        IImmutableList<string> queryFailedQueues,
        IImmutableList<string> microsoftPools,
        IImmutableList<string> oneEsPools,
        IImmutableList<EstimatedPipelineDuration> durations,
        bool criticalIssues
    )
    {
        LongestQueues = longestQueues;
        Repo = repo;
        PullRequest = pullRequest;
        SentimentParameters = new UserSentimentParameters(repo, commitHash, pullRequest, false);
        QueryFailedQueues = queryFailedQueues;

        if (queues is not null && onPremQueues is not null)
            QueueView = new HelixQueueView(queues, onPremQueues, Repo);

        BuildInfoView = new BuildInfoView(microsoftPools, oneEsPools);
        EstimatedPipelineDurations = new EstimatedPipelineDurationView(durations, criticalIssues);
    }

    public string GrafanaDashboardUrl { get; } = GrafanaUrlGenerator.QueueMonitorUrl;

    public bool HighWaitTimeError => LongestQueues is null || !LongestQueues.Any();

    public IImmutableList<HighestWaitTimeQueueInfoView> LongestQueues { get; }

    public string Repo { get; }

    public string PullRequest { get; }

    public HelixQueueView QueueView { get; }

    public bool QueueListError => QueueView is null;

    public UserSentimentParameters SentimentParameters { get; set; }

    public IImmutableList<string> QueryFailedQueues { get; }

    public bool RepoHasQueues => QueueView is { QueueCount: > 0 };

    public EstimatedPipelineDurationView EstimatedPipelineDurations { get; }

    public bool HasEstimatedPipelineDurations => EstimatedPipelineDurations.Durations.Count > 0;

    public BuildInfoView BuildInfoView { get; }
}

public class HelixQueueView
{
    public HelixQueueView(IImmutableList<string> queues, IImmutableList<string> onPremQueues, string repo)
    {
        Queues = queues;
        OnPremQueues = onPremQueues;
        Repo = repo;
    }

    public IImmutableList<string> Queues { get; }

    public IImmutableList<string> OnPremQueues { get; }

    public int QueueCount => Queues.Count + OnPremQueues.Count;

    public string Repo { get; }
}

public class HighestWaitTimeQueueInfoView
{
    public HighestWaitTimeQueueInfoView(string queueName, TimeSpan waitTime, double movingAverageDiff)
    {
        QueueName = queueName;
        WaitTime = waitTime;
        MovingAverageDiff = movingAverageDiff.ToString("0.##");
        MovingAverageStatus = movingAverageDiff > 0;
    }

    public string QueueName { get; }

    public TimeSpan WaitTime { get; }

    public string MovingAverageDiff { get; }

    public bool MovingAverageStatus { get; }
}

public class EstimatedPipelineDurationView
{
    public EstimatedPipelineDurationView(IImmutableList<EstimatedPipelineDuration> durations, bool criticalIssues)
    {
        Durations = durations.Select(x => new View(x)).ToImmutableList();
        CriticalIssue = criticalIssues;
    }

    public IImmutableList<View> Durations { get; }
    public bool HasHighVariancePipeline => Durations.Any(x => x.HighVariance);

    public bool CriticalIssue { get; }

    public class View
    {
        public View(EstimatedPipelineDuration duration)
        {
            Duration = duration;
        }

        public EstimatedPipelineDuration Duration { get; }

        public bool HighVariance => Duration.Mean < 1.5 * Duration.ConfidenceInterval;
    }
}

public class BuildInfoView
{
    public BuildInfoView(IImmutableList<string> microsoftPools, IImmutableList<string> oneEsPools)
    {
        MicrosoftPools = microsoftPools;
        OneESPools = oneEsPools;
    }

    public IImmutableList<string> MicrosoftPools { get; }
    public bool HasMicrosoftPool => MicrosoftPools.Count > 0;

    public IImmutableList<string> OneESPools { get; }
    public bool HasOneESHostedPool => OneESPools.Count > 0;
}
