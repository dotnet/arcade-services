// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class GetChannelsOperation : Operation
    {
        GetChannelsCommandLineOptions _options;
        public GetChannelsOperation(GetChannelsCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        /// Retrieve information about channels
        /// </summary>
        /// <param name="options">Command line options</param>
        /// <returns>Process exit code.</returns>
        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                var allChannels = await remote.GetChannelsAsync();

                // Write out a simple list of each channel's name
                foreach (var channel in allChannels.OrderBy(c => c.Name))
                {
                    // Pad so that id's up to 9999 will result in consistent
                    // listing
                    string idPrefix = $"({channel.Id})".PadRight(7);
                    Console.WriteLine($"{idPrefix}{channel.Name}");
                }

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed to retrieve channels");
                return Constants.ErrorCode;
            }
        }
    }
}
