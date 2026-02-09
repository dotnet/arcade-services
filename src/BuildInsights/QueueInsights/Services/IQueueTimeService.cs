// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using QueueInsights.Models;

namespace QueueInsights.Services;

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
