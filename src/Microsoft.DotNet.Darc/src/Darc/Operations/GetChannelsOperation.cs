// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
                switch (_options.OutputFormat)
                {
                    case DarcOutputType.json:
                        WriteJsonChannelList(allChannels);
                        break;
                    case DarcOutputType.yaml:
                        WriteYamlChannelList(allChannels);
                        break;
                    default:
                        throw new NotImplementedException($"Output format {_options.OutputFormat} not supported for get-channels");
                }

                return Constants.SuccessCode;
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error: Failed to retrieve channels");
                return Constants.ErrorCode;
            }
        }

        private void WriteJsonChannelList(IEnumerable<Channel> allChannels)
        {
            var channelJson = new
            {
                channels = allChannels.OrderBy(c => c.Name).Select(channel =>
                    new
                    {
                        id = channel.Id,
                        name = channel.Name
                    })
            };

            Console.WriteLine(JsonConvert.SerializeObject(channelJson, Formatting.Indented));
        }

        private void WriteYamlChannelList(IEnumerable<Channel> allChannels)
        {
            // Write out a simple list of each channel's name
            foreach (var channel in allChannels.OrderBy(c => c.Name))
            {
                // Pad so that id's up to 9999 will result in consistent
                // listing
                string idPrefix = $"({channel.Id})".PadRight(7);
                Console.WriteLine($"{idPrefix}{channel.Name}");
            }
        }
    }
}
