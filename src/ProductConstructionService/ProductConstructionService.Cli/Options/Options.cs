// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductConstructionService.Cli.Operations;

namespace ProductConstructionService.Cli.Options;
internal abstract class Options
{
    public virtual Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddLogging(logging => logging.AddConsole());
        return Task.FromResult(services);
    }

    public abstract IOperation GetOperation(IServiceProvider sp);
}
