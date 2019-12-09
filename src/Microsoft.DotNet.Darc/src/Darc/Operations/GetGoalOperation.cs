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
    internal class GetGoalOperation : Operation
    {
        GetGoalCommandLineOptions _options;
        public GetGoalOperation(GetGoalCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }
        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);
                Goal goalInfo = await remote.GetGoalAsync(_options.Channel, _options.DefinitionId);
                if(goalInfo != null)
                {
                    Console.Write(goalInfo.Minutes);
                }
                return Constants.SuccessCode;
            }
            catch (RestApiException e) when (e.Response.Status == (int)HttpStatusCode.NotFound)
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
