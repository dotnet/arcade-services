// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Data;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Logging;
using QueueInsights.Models;

namespace QueueInsights;

/// <summary>
///     Obtains the Work Item Wait Times for Helix queues.
/// </summary>
public interface IQueueTimeService
{
    /// <summary>
    ///     Gets the <paramref name="percentile" /> of work item wait times for the specified helix <paramref name="queue" />.
    /// </summary>
    /// <param name="queues">The Helix queues to obtain the current work item wait times for.</param>
    /// <param name="percentile">The percentile of work item wait times to get. Default is the 95th percentile.</param>
    /// <returns>The current work item wait time, or null if the work item wait time could not be obtained.</returns>
    public Task<QueueInsightsResult<Dictionary<string, TimeSpan>>> GetWorkItemWaitTimesAsync(string[] queues,
        uint percentile = 95);

    /// <summary>
    ///     Gets the rolling average of the <paramref name="percentile" /> of work item wait times over
    ///     <paramref name="movingAveragePeriodHrs" /> for the specified <paramref name="queue" />.
    /// </summary>
    /// <param name="queue">The Helix queue.</param>
    /// <param name="movingAveragePeriodHrs">The duration of the moving average, in hours. Default is 24 hours.</param>
    /// <param name="percentile">The percentile of work item wait times to get. Default is the 95th percentile.</param>
    /// <returns>The current work item wait time, or null if the work item wait time could not be obtained.</returns>
    public Task<TimeSpan?> GetWorkItemWaitTimeMovingAverageAsync(string queue, uint movingAveragePeriodHrs = 24,
        uint percentile = 95);

    /// <summary>
    ///     Computed the estimated duration of the <paramref name="pipelineIds" />. The durations are expressed as a mean and
    ///     the confidence interval.
    /// </summary>
    /// <param name="pipelineIds">A set of pipeline (definition) IDs to get the duration for.</param>
    /// <param name="targetBranch">The target branch to get the pipeline duration for.</param>
    /// <param name="prob">
    ///     The maximum probability [0, 1] that the estimated pipeline duration can not be in the provided range. This
    ///     is the maximum probability, meaning that in the worst case, the estimate is wrong no more than
    ///     <paramref name="prob" />
    ///     percent.
    /// </param>
    /// <returns></returns>
    public Task<IImmutableList<EstimatedPipelineDuration>> GetEstimatedPipelineDurations(
        IImmutableSet<int> pipelineIds, string targetBranch, double prob = 0.5);
}

public class QueueTimeService : IQueueTimeService
{
    private readonly IKustoClientProvider _kustoClientProvider;
    private readonly ILogger<QueueTimeService> _logger;

    public QueueTimeService(IKustoClientProvider kustoClientProvider, ILogger<QueueTimeService> logger)
    {
        _kustoClientProvider = kustoClientProvider;
        _logger = logger;
    }

    public async Task<TimeSpan?> GetWorkItemWaitTimeMovingAverageAsync(string queue,
        uint movingAveragePeriodHrs = 24,
        uint percentile = 95)
    {
        var queryParameters = new[]
        {
            new KustoParameter("_queue", queue.ToLowerInvariant(), KustoDataType.String),
            new KustoParameter("_percentile", percentile, KustoDataType.Int),
            new KustoParameter("_maPeriod", movingAveragePeriodHrs, KustoDataType.Int)
        };

        const string query = @"WorkItems
| where QueueName == _queue
| where Started > ago(48h)
| project
    QueueName = tolower(QueueName),
    duration = datetime_diff('second', Started, Queued),
    Started
| evaluate rolling_percentile(duration, _percentile, Started, 1hr, _maPeriod)
| top 1 by Started";

        TimeSpan? ret;
        using IDataReader results =
            await _kustoClientProvider.ExecuteKustoQueryAsync(new KustoQuery(query, queryParameters));


        // If the query fails, presumably due to an invalid queue name we'll return null.
        if (results.Read())
        {
            ret = TimeSpan.FromSeconds(results.GetInt64(1));
        }
        else
        {
            _logger.LogWarning($"Query to obtain WorkItem wait time for the queue {queue} returned null.");
            ret = null;
        }

        return ret;
    }

    public async Task<IImmutableList<EstimatedPipelineDuration>> GetEstimatedPipelineDurations(
        IImmutableSet<int> pipelineIds, string targetBranch, double prob = 0.5)
    {
        if (prob is <= 0 or > 1)
        {
            throw new ArgumentException("The probability should be a value between [0, 1].", nameof(prob));
        }

        var records = new List<EstimatedPipelineDuration>();

        double k = Math.Sqrt(prob) / prob;

        if (pipelineIds.Count > 0)
        {
/*
* This query does the following:
*
*  1. Filter data. Select the builds from the public project, caused by a PR against the target branch,
*     succeeded, and is matching the definitionID we care about.
*  2. Compute the duration of the pipeline in seconds.
*  3. Join this data with the 5th and 95th percentile of the durations, with respect to the pipeline.
*  4. Choose pipelines that have more than 30 runs, so the Central Limit Theorem applies.
*  5. Filter outliers by ensuring the pipeline duration is between the 5th and 95th percentile.
*  6. Compute the mean and the confidence interval, using Chebychev's Inequality Theorem.
*  7. Cast to the correct types, and format the definition name correctly.
*/
            string query = @$"TimelineBuilds
| where Project == ""public""
| where Reason == ""pullRequest""
| where TargetBranch == _targetBranch
| where Result == ""succeeded""
| where DefinitionId in ({string.Join(", ", pipelineIds)})
| extend PipelineDuration = datetime_diff('second', FinishTime, StartTime) * 1s
| project-keep Definition, PipelineDuration, DefinitionId, FinishTime
| join kind=inner (
    TimelineBuilds
    | where Project == ""public""
    | where Reason == ""pullRequest""
    | where TargetBranch == _targetBranch
    | where Result == ""succeeded""
    | project
        DefinitionId,
        PipelineDuration = datetime_diff('second', FinishTime, StartTime) * 1s
    | summarize
        Bottom5 = percentile(PipelineDuration, 5),
        Top95 = percentile(PipelineDuration, 95),
        Count = count()
        by DefinitionId
    | where Count >= 30)
    on DefinitionId
| where PipelineDuration between (Bottom5 .. Top95)
| summarize
    Mean = avg(PipelineDuration),
    ConfidenceInterval = _k * totimespan(stdevp(PipelineDuration)),
    Definition=arg_max(FinishTime, Definition)[1]
    by DefinitionId
| project
    DefinitionId = toint(DefinitionId),
    Definition =tostring(split(Definition, '\\')[-1]),
    Mean,
    ConfidenceInterval";

            using IDataReader data = await _kustoClientProvider.ExecuteKustoQueryAsync(new KustoQuery(query,
                new KustoParameter("_k", k, KustoDataType.Real),
                new KustoParameter("_targetBranch", targetBranch, KustoDataType.String)));

            while (data.Read())
            {
                records.Add(new EstimatedPipelineDuration(
                    data.GetInt32(0),
                    data.GetString(1),
                    (TimeSpan)data.GetValue(2),
                    (TimeSpan)data.GetValue(3)));
            }

        }

        return records.ToImmutableList();
    }

    public async Task<QueueInsightsResult<Dictionary<string, TimeSpan>>> GetWorkItemWaitTimesAsync(string[] queues,
        uint percentile)
    {
        if (queues.Length == 0)
            return new QueueInsightsResult<Dictionary<string, TimeSpan>>(
                new Dictionary<string, TimeSpan>(),
                new List<string>(0));

        var queryParameters = new[]
        {
            new KustoParameter("_percentile", percentile, KustoDataType.Int)
        };

        string query = @$"WorkItems
| where QueueName in ({string.Join(", ", queues.Select(x => $"\"{x.ToLowerInvariant()}\""))})
| where Started > ago(1hr)
| project
    QueueName = tolower(QueueName),
    duration = datetime_diff('second', Started, Queued), Started
| summarize percentile(duration, _percentile) by QueueName";

        using IDataReader data =
            await _kustoClientProvider.ExecuteKustoQueryAsync(new KustoQuery(query, queryParameters));

        var waitTimes = new Dictionary<string, TimeSpan>();

        while (data.Read()) waitTimes[data.GetString(0)] = TimeSpan.FromSeconds(data.GetInt64(1));

        ImmutableList<string> failedQueues = queues.Where(queue => !waitTimes.ContainsKey(queue)).ToImmutableList();

        return new QueueInsightsResult<Dictionary<string, TimeSpan>>(waitTimes, failedQueues);
    }
}
