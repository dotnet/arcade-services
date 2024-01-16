// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.ServiceFabric.ServiceHost;

namespace DependencyUpdateErrorProcessor;

public static class Program
{
    /// <summary>
    /// This is the entry point of the service host process.
    /// </summary>
    private static void Main()
    {
        ServiceHost.Run(
            host =>
            {
                host.RegisterStatefulService<DependencyUpdateErrorProcessor>("DependencyUpdateErrorProcessorType");
            });
    }
}
