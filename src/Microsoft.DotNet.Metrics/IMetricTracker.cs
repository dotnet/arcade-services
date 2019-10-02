// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Microsoft.DotNet.Metrics
{
    /// <summary>
    /// Interface for reporting metrics to the shared arcade metrics store
    /// </summary>
    public interface IMetricTracker
    {
        /// <summary>
        /// Record a single data point for the named metric, with optional dimensions. Metric value
        /// may be pre-aggregated depending on implementation
        /// </summary>
        /// <param name="name">Name of the metric to report</param>
        /// <param name="value">Value for this instance of the metric</param>
        /// <param name="dimensions">Optional dimensions. Aggregations will never happen
        /// for metrics with different values for dimensions. Should generally not vary with time/work.
        /// An example would be the static queue id that something is pulled from</param>
        void TrackMetric(string name, double value, IDictionary<string, string> dimensions = default);
    }
}
