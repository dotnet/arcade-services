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
    /// <summary>
    /// Implements an operation to get information about the default channel associations.
    /// </summary>
    internal class GetDefaultChannelsOperation : Operation
    {
        GetDefaultChannelsCommandLineOptions _options;
        public GetDefaultChannelsOperation(GetDefaultChannelsCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        /// <summary>
        /// Retrieve information about the default association between builds of a specific branch/repo
        /// and a channel.
        /// </summary>
        /// <returns></returns>
        public override async Task<int> ExecuteAsync()
        {
            try
            {
                IRemote remote = RemoteFactory.GetBarOnlyRemote(_options, Logger);

                IEnumerable<DefaultChannel> defaultChannels = (await remote.GetDefaultChannelsAsync())
                    .Where(defaultChannel =>
                    {
                        return (string.IsNullOrEmpty(_options.SourceRepository) ||
                            defaultChannel.Repository.Contains(_options.SourceRepository, StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrEmpty(_options.Branch) ||
                            defaultChannel.Branch.Contains(_options.Branch, StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrEmpty(_options.Channel) ||
                            defaultChannel.Channel.Name.Contains(_options.Channel, StringComparison.OrdinalIgnoreCase));
                    })
                    .OrderBy(df => df.Repository);

                if (defaultChannels.Count() == 0)
                {
                    Console.WriteLine("No matching channels were found.");
                }

                // Write out a simple list of each channel's name
                foreach (DefaultChannel defaultChannel in defaultChannels)
                {
                    Console.WriteLine(OutputHelpers.GetDefaultChannelDescriptionString(defaultChannel));
                }

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed to retrieve default channel information.");
                return Constants.ErrorCode;
            }
        }
    }
}
