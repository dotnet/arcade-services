// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.Internal.Health
{
    public class HealthReport
    {
        public HealthReport(string serviceName, string instance, string subStatusName, HealthStatus health, string message, DateTimeOffset asOf)
        {
            Service= serviceName;
            Instance = instance;
            SubStatus = subStatusName;
            Health = health;
            Message = message;
            AsOf = asOf;
        }

        public string Service { get; }
        public string Instance { get; }
        public string SubStatus{ get; }
        public HealthStatus Health { get; }
        public string Message { get; }
        public DateTimeOffset AsOf { get; }
    }
}
