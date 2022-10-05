// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ApplicationInsights.Channel;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost;

public class AppInsightsFlushLifecycle : Lifecycle
{
    private readonly ITelemetryChannel _channel;

    public AppInsightsFlushLifecycle(ITelemetryChannel channel)
    {
        _channel = channel;
    }

    public override void OnStopping()
    {
        _channel.Flush();
    }
}
