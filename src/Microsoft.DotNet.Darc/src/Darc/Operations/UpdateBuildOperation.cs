// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class UpdateBuildOperation : Operation
    {
        UpdateBuildCommandLineOptions _options;
        public UpdateBuildOperation(UpdateBuildCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public async override Task<int> ExecuteAsync()
        {
            if (!(_options.Released ^ _options.NotReleased))
            {
                Console.WriteLine("Please specify either --released or --not-released.");
                return Constants.ErrorCode;
            }

            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                Build updatedBuild = await remote.UpdateBuildAsync(_options.Id, new BuildUpdate { Released = _options.Released });

                Console.WriteLine($"Updated build {_options.Id} with new information.");
                Console.WriteLine(UxHelpers.GetTextBuildDescription(updatedBuild));
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error: Failed to update build with id '{_options.Id}'");
                return Constants.ErrorCode;
            }

            return Constants.SuccessCode;
        }
    }
}
