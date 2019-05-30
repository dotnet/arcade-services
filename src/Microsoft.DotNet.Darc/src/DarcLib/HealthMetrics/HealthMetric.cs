// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.DarcLib.HealthMetrics
{
    public enum HealthResult
    {
        Passed,
        Failed,
        Warning
    }

    public abstract class HealthMetric
    {
        public HealthMetric(ILogger logger, IRemoteFactory remoteFactory)
        {
            Logger = logger;
            RemoteFactory = remoteFactory;
        }

        protected readonly ILogger Logger;
        protected readonly IRemoteFactory RemoteFactory;

        public abstract string MetricName { get; }

        public abstract string MetricDescription { get; }

        public HealthResult Result { get; protected set; }

        public abstract Task EvaluateAsync();
    }
}
