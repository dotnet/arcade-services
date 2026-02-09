// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace QueueInsights;

internal class GrafanaUrlGenerator
{
    public static readonly string QueueMonitorUrl =
        "https://dnceng-grafana-eraubnb4dkatgnfn.wus2.grafana.azure.com/d/queues/queue-monitor";

    /// <summary>
    ///     Builds a URL to the Grafana dashboard for the specified queue.
    /// </summary>
    /// <param name="queue">The queue to show the dashboard for.</param>
    /// <returns>A URL to the Grafana dashboard for the specified queue.</returns>
    public static string GetGrafanaUrlForQueue(string queue)
    {
        var builder = new UriBuilder(QueueMonitorUrl)
        {
            Query = $"var-QueueName={queue.ToLowerInvariant()}"
        };

        return builder.Uri.AbsoluteUri;
    }
}
