// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Options;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.Darc.Operations
{
    public abstract class Operation : IDisposable
    {
        private readonly ServiceProvider _provider;

        protected ILogger<Operation> Logger { get; }

        protected Operation(CommandLineOptions options)
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

            ServiceCollection collection = new ServiceCollection();
            collection.AddLogging(b => b.AddConsole().AddFilter(l => l >= level));
            _provider = collection.BuildServiceProvider();
            Logger = _provider.GetRequiredService<ILogger<Operation>>();
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
                _provider?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
