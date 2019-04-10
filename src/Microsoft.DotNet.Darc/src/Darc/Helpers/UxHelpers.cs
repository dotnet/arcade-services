// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc
{
    public static class UxHelpers
    {
        /// <summary>
        ///     Resolve a channel substring to an exact channel, or print out potential names if more than one, or none, match.
        /// </summary>
        /// <param name="remote">Remote for retrieving channels</param>
        /// <param name="desiredChannel">Desired channel</param>
        /// <returns>Channel, or null if no channel was matched.</returns>
        public static async Task<Channel> ResolveSingleChannel(IRemote remote, string desiredChannel)
        {
            // Retrieve the channel by name, matching substring. If more than one channel 
            // matches, then let the user know they need to be more specific
            IEnumerable<Channel> channels = (await remote.GetChannelsAsync());
            List<Channel> matchingChannels = channels.Where(c => c.Name.Contains(desiredChannel, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!matchingChannels.Any())
            {
                Console.WriteLine($"No channels found with name containing '{desiredChannel}'");
                Console.WriteLine("Available channels:");
                foreach (Channel channel in channels)
                {
                    Console.WriteLine($"  {channel.Name}");
                }
                return null;
            }
            else if (matchingChannels.Count != 1)
            {
                Console.WriteLine($"Multiple channels found with name containing '{desiredChannel}', please select one");
                foreach (Channel channel in matchingChannels)
                {
                    Console.WriteLine($"  {channel.Name}");
                }
                return null;
            }
            else
            {
                return matchingChannels.Single();
            }
        }
    }
}
