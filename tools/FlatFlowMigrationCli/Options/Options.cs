// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FlatFlowMigrationCli.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ProductConstructionService.Common;

namespace FlatFlowMigrationCli.Options;

internal abstract class Options
{
    public virtual Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddLogging(logging => logging.AddConsole());

        services.AddLogging(b => b
            .AddConsole(o => o.FormatterName = CompactConsoleLoggerFormatter.FormatterName)
            .AddConsoleFormatter<CompactConsoleLoggerFormatter, SimpleConsoleFormatterOptions>());

        services.AddTransient<VmrDependencyResolver>();

        return Task.FromResult(services);
    }

    public abstract IOperation GetOperation(IServiceProvider sp);
}
