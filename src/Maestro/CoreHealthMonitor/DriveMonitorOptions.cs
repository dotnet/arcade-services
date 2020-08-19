// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace CoreHealthMonitor
{
    public class DriveMonitorOptions
    {
        public long MinimumFreeSpaceBytes { get; set; }
    }

    public class MemoryDumpOptions
    {
        public string ContainerUri { get; set; }
    }
}
