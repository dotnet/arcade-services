// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.DotNet.Maestro.Client;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class SetGoalOperation : Operation
    {
        private SetGoalCommandLineOptions _options;

        public SetGoalOperation(SetGoalCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        ///  Sets Goal in minutes for a definition in a Channel.
        /// </summary>
        /// <returns>Process exit code.</returns>
        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);
                Goal goalInfo = await remote.SetGoalAsync(_options.Channel, _options.DefinitionId, _options.Minutes);
                Console.Write(goalInfo.Minutes);
                return Constants.SuccessCode;
            }
            catch (RestApiException e) when (e.Response.Status == (int) HttpStatusCode.NotFound)
            {
                Logger.LogError(e, $"Cannot find Channel '{_options.Channel}'.");
                return Constants.ErrorCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Unable to create goal for Channel : '{_options.Channel}' and DefinitionId : '{_options.DefinitionId}'.");
                return Constants.ErrorCode;
            }
        }
    }
}
