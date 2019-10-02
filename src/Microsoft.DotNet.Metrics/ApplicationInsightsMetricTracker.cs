// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Metrics;

namespace Microsoft.DotNet.Metrics
{
    /// <summary>
    ///     Implementation of <see cref="IMetricTracker" /> that reports metrics to application insights.
    ///     Using pre-aggregated metrics
    ///     https://docs.microsoft.com/en-us/azure/azure-monitor/app/pre-aggregated-metrics-log-metrics#pre-aggregated-metrics
    /// </summary>
    public class ApplicationInsightsMetricTracker : IMetricTracker
    {
        private readonly TelemetryClient _client;

        public ApplicationInsightsMetricTracker(TelemetryClient client)
        {
            _client = client;
        }

        /// <inheritdoc />
        public void TrackMetric(string name, double value, IDictionary<string, string> dimensions = default)
        {
            if (dimensions?.Count > 9)
            {
                throw new ArgumentException("Maximum of 4 dimensions supported", nameof(dimensions));
            }

            if (dimensions == null || dimensions.Count == 0)
            {
                // If we have no dimension, do it quick and dirty
                _client.GetMetric(name).TrackValue(value);
                return;
            }

            // Sort the keys so we get the right aggregator. It's important that A=1,B=2 is the same as B=2,A=1
            // If we see a lot of perf impact from creating and sorting these dictionaries every time,
            // we might have to expose out overloads to take everything inline, but that will be messy and really
            // lead to a hard to understand interface, so I'm not doing that right now
            List<KeyValuePair<string, string>> pairs = dimensions.OrderBy(d => d.Key).ToList();

            // Yes, this long switch is necessary, since each number of dimensions is it's own overload
            switch (pairs.Count)
            {
                case 1:
                    _client.GetMetric(name, pairs[0].Key).TrackValue(value, pairs[0].Value);
                    break;
                case 2:
                    _client.GetMetric(name, pairs[0].Key, pairs[1].Key)
                        .TrackValue(value, pairs[0].Value, pairs[1].Value);
                    break;
                case 3:
                    _client.GetMetric(name, pairs[0].Key, pairs[1].Key, pairs[2].Key)
                        .TrackValue(value, pairs[0].Value, pairs[1].Value, pairs[2].Value);
                    break;
                case 4:
                    _client.GetMetric(name, pairs[0].Key, pairs[1].Key, pairs[2].Key, pairs[3].Key)
                        .TrackValue(value, pairs[0].Value, pairs[1].Value, pairs[2].Value, pairs[3].Value);
                    break;
                case 5:
                    _client.GetMetric(
                            new MetricIdentifier(
                                MetricIdentifier.DefaultMetricNamespace,
                                name,
                                pairs[0].Key,
                                pairs[1].Key,
                                pairs[2].Key,
                                pairs[3].Key,
                                pairs[4].Key
                            ))
                        .TrackValue(
                            value,
                            pairs[0].Value,
                            pairs[1].Value,
                            pairs[2].Value,
                            pairs[3].Value,
                            pairs[4].Value
                        );
                    break;
                case 6:
                    _client.GetMetric(
                            new MetricIdentifier(
                                MetricIdentifier.DefaultMetricNamespace,
                                name,
                                pairs[0].Key,
                                pairs[1].Key,
                                pairs[2].Key,
                                pairs[3].Key,
                                pairs[4].Key,
                                pairs[5].Key
                            ))
                        .TrackValue(
                            value,
                            pairs[0].Value,
                            pairs[1].Value,
                            pairs[2].Value,
                            pairs[3].Value,
                            pairs[4].Value,
                            pairs[5].Value
                        );
                    break;
                case 7:
                    _client.GetMetric(
                            new MetricIdentifier(
                                MetricIdentifier.DefaultMetricNamespace,
                                name,
                                pairs[0].Key,
                                pairs[1].Key,
                                pairs[2].Key,
                                pairs[3].Key,
                                pairs[4].Key,
                                pairs[5].Key,
                                pairs[6].Key
                            ))
                        .TrackValue(
                            value,
                            pairs[0].Value,
                            pairs[1].Value,
                            pairs[2].Value,
                            pairs[3].Value,
                            pairs[4].Value,
                            pairs[5].Value,
                            pairs[6].Value
                        );
                    break;
                case 8:
                    _client.GetMetric(
                            new MetricIdentifier(
                                MetricIdentifier.DefaultMetricNamespace,
                                name,
                                pairs[0].Key,
                                pairs[1].Key,
                                pairs[2].Key,
                                pairs[3].Key,
                                pairs[4].Key,
                                pairs[5].Key,
                                pairs[6].Key,
                                pairs[7].Key
                            ))
                        .TrackValue(
                            value,
                            pairs[0].Value,
                            pairs[1].Value,
                            pairs[2].Value,
                            pairs[3].Value,
                            pairs[4].Value,
                            pairs[5].Value,
                            pairs[6].Value,
                            pairs[7].Value
                        );
                    break;
                case 9:
                    _client.GetMetric(
                            new MetricIdentifier(
                                MetricIdentifier.DefaultMetricNamespace,
                                name,
                                pairs[0].Key,
                                pairs[1].Key,
                                pairs[2].Key,
                                pairs[3].Key,
                                pairs[4].Key,
                                pairs[5].Key,
                                pairs[6].Key,
                                pairs[7].Key,
                                pairs[8].Key
                            ))
                        .TrackValue(
                            value,
                            pairs[0].Value,
                            pairs[1].Value,
                            pairs[2].Value,
                            pairs[3].Value,
                            pairs[4].Value,
                            pairs[5].Value,
                            pairs[6].Value,
                            pairs[7].Value,
                            pairs[8].Value
                        );
                    break;
                case 10:
                    _client.GetMetric(
                            new MetricIdentifier(
                                MetricIdentifier.DefaultMetricNamespace,
                                name,
                                pairs[0].Key,
                                pairs[1].Key,
                                pairs[2].Key,
                                pairs[3].Key,
                                pairs[4].Key,
                                pairs[5].Key,
                                pairs[6].Key,
                                pairs[7].Key,
                                pairs[8].Key,
                                pairs[9].Key
                            ))
                        .TrackValue(
                            value,
                            pairs[0].Value,
                            pairs[1].Value,
                            pairs[2].Value,
                            pairs[3].Value,
                            pairs[4].Value,
                            pairs[5].Value,
                            pairs[6].Value,
                            pairs[7].Value,
                            pairs[8].Value,
                            pairs[9].Value
                        );
                    break;
                default:
                    throw new InvalidOperationException("Already checked");
            }
        }
    }
}
