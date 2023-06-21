// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceFabric.Data;
using Microsoft.ServiceFabric.Data.Collections;
using Newtonsoft.Json.Linq;
using Octokit;

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
