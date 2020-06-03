using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public class FakeChannel : ITelemetryChannel
    {
        public void Dispose()
        {
        }

        public void Send(ITelemetry item)
        {
            Telemetry.Add(item);
        }

        public void Flush()
        {
        }

        public bool? DeveloperMode { get; set; }
        public string EndpointAddress { get; set; }

        public List<ITelemetry> Telemetry { get; } = new List<ITelemetry>();
    }
}
