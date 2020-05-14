// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    class DefaultChannelStatusOperation : UpdateDefaultChannelBaseOperation
    {
        DefaultChannelStatusCommandLineOptions _options;

        public DefaultChannelStatusOperation(DefaultChannelStatusCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        /// Implements the default channel enable/disable operation
        /// </summary>
        /// <param name="options"></param>
        public override async Task<int> ExecuteAsync()
        {
            if ((_options.Enable && _options.Disable) ||
                (!_options.Enable && !_options.Disable))
            {
                Console.WriteLine("Please specify either --enable or --disable");
                return Constants.ErrorCode;
            }

            IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

            try
            {
                DefaultChannel resolvedChannel = await ResolveSingleChannel();
                if (resolvedChannel == null)
                {
                    return Constants.ErrorCode;
                }

                bool enabled;
                if (_options.Enable)
                {
                    if (resolvedChannel.Enabled)
                    {
                        Console.WriteLine($"Default channel association is already enabled");
                        return Constants.ErrorCode;
                    }
                    enabled = true;
                }
                else
                {
                    if (!resolvedChannel.Enabled)
                    {
                        Console.WriteLine($"Default channel association is already disabled");
                        return Constants.ErrorCode;
                    }
                    enabled = false;
                }

                await remote.UpdateDefaultChannelAsync(resolvedChannel.Id, enabled: enabled);

                Console.WriteLine($"Default channel association has been {(enabled ? "enabled" : "disabled")}.");

                return Constants.SuccessCode;
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine(e.Message);
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed enable/disable default channel association.");
                return Constants.ErrorCode;
            }
        }
    }
}
