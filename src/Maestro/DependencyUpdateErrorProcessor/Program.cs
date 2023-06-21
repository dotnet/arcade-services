// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Maestro.Data;
using Microsoft.DncEng.Configuration.Extensions;
using Microsoft.DotNet.GitHub.Authentication;
using Microsoft.DotNet.Internal.DependencyInjection;
using Microsoft.DotNet.ServiceFabric.ServiceHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octokit;

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
