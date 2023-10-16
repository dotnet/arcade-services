// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Arcade.Common;
using Microsoft.DotNet.Darc.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

public abstract class Operation : IDisposable
{
    protected ServiceProvider Provider { get; }

    protected ILogger<Operation> Logger { get; }

    protected Operation(ICommandLineOptions options, IServiceCollection? services = null)
    {
        // Because the internal logging in DarcLib tends to be chatty and non-useful,
        // we remap the --verbose switch onto 'info', --debug onto highest level, and the
        // default level onto warning
        LogLevel level = LogLevel.Warning;
        if (options.Debug)
        {
            level = LogLevel.Debug;
        }
        else if (options.Verbose)
        {
            level = LogLevel.Information;
        }

        if (!IsOutputFormatSupported(options.OutputFormat))
        {
            throw new NotImplementedException($"Output format type '{options.OutputFormat}' not yet supported for this operation.\r\nPlease raise a new issue in https://github.com/dotnet/arcade/issues/.");
        }

        services ??= new ServiceCollection();
        services.AddLogging(b => b
            .AddConsole(o => o.FormatterName = CompactConsoleLoggerFormatter.FormatterName)
            .AddConsoleFormatter<CompactConsoleLoggerFormatter, SimpleConsoleFormatterOptions>()
            .SetMinimumLevel(level));
            
        services.AddSingleton(options);

        Provider = services.BuildServiceProvider();
        Logger = Provider.GetRequiredService<ILogger<Operation>>();
    }

    public abstract Task<int> ExecuteAsync();

    /// <summary>
    ///  Indicates whether the requested output format is supported.
    /// </summary>
    /// <param name="outputFormat">The desired output format.</param>
    /// <returns>
    ///  The base implementations returns <see langword="true"/> for <see cref="DarcOutputType.text"/>; otherwise <see langword="false"/>.
    /// </returns>
    protected virtual bool IsOutputFormatSupported(DarcOutputType outputFormat)
        => outputFormat switch
        {
            DarcOutputType.text => true,
            _ => false
        };

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Provider?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
