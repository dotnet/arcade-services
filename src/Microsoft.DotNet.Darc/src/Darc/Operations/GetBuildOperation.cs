// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class GetBuildOperation : Operation
    {
        GetBuildCommandLineOptions _options;
        public GetBuildOperation(GetBuildCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        ///     Get a specific build of a repository
        /// </summary>
        /// <returns>Process exit code.</returns>
        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                Build build = await remote.GetBuildAsync(_options.Id);
                if (build != null)
                {
                    OutputHelpers.PrintBuild(build);
                }
                else
                {
                    Console.WriteLine($"Could not find build with id '{_options.Id}'");
                    return Constants.ErrorCode;
                }

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error: Failed to retrieve build with id '{_options.Id}'");
                return Constants.ErrorCode;
            }
        }
    }
}
