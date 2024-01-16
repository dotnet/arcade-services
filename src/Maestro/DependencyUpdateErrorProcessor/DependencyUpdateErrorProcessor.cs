// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.ServiceFabric.ServiceHost;

namespace DependencyUpdateErrorProcessor;

/// <summary>
/// An instance of this class is created for each service replica by the Service Fabric runtime.
/// </summary>
public sealed class DependencyUpdateErrorProcessor : IServiceImplementation
{
    public DependencyUpdateErrorProcessor() { }

    /// <summary>
    /// This will run after the MaxValue is reached. In this case it will never run.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(TimeSpan.MaxValue);
    }
}
