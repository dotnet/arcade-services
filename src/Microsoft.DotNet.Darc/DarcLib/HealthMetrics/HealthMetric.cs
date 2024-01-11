// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.DarcLib.HealthMetrics;

public enum HealthResult
{
    Passed,
    Failed,
    Warning
}

public abstract class HealthMetric
{
    public HealthMetric(
        ILogger logger,
        IRemoteFactory remoteFactory,
        IBasicBarClientFactory barClientFactory)
    {
        Logger = logger;
        RemoteFactory = remoteFactory;
        BarClientFactory = barClientFactory;
    }

    protected readonly ILogger Logger;
    protected readonly IRemoteFactory RemoteFactory;
    protected readonly IBasicBarClientFactory BarClientFactory;

    public abstract string MetricName { get; }

    public abstract string MetricDescription { get; }

    public HealthResult Result { get; protected set; }

    public abstract Task EvaluateAsync();
}
