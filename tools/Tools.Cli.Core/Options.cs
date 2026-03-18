// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Maestro.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Tools.Cli.Core;

public class Options
{
    public virtual Task<IServiceCollection> RegisterServices(IServiceCollection services)
    {
        services.AddLogging(b => b
            .AddConsole(o => o.FormatterName = CompactConsoleLoggerFormatter.FormatterName)
            .AddConsoleFormatter<CompactConsoleLoggerFormatter, SimpleConsoleFormatterOptions>());
        return Task.FromResult(services);
    }

    public virtual IOperation GetOperation(IServiceProvider sp)
    {
        var attribute = GetType().GetCustomAttribute<OperationAttribute>();
        if (attribute is not null)
        {
            return (IOperation)ActivatorUtilities.CreateInstance(sp, attribute.OperationType, [this]);
        }

        throw new InvalidOperationException(
            $"No OperationAttribute found on {GetType().Name}. Either apply [Operation(typeof(YourOperation))] or override GetOperation().");
    }
}
