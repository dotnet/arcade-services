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
    internal class DeleteDefaultChannelOperation : UpdateDefaultChannelBaseOperation
    {
        DeleteDefaultChannelCommandLineOptions _options;
        public DeleteDefaultChannelOperation(DeleteDefaultChannelCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                DefaultChannel resolvedChannel = await ResolveSingleChannel();
                if (resolvedChannel == null)
                {
                    return Constants.ErrorCode;
                }

                await remote.DeleteDefaultChannelAsync(resolvedChannel.Id);

                return Constants.SuccessCode;
            }
            catch (AuthenticationException e)
            {
                Console.WriteLine(e.Message);
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed remove the default channel association.");
                return Constants.ErrorCode;
            }
        }
    }
}
