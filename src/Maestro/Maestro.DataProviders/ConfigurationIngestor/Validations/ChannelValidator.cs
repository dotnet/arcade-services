// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.Data.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestor.Validations;

public class ChannelValidator
{
    /// <summary>
    /// Validates a collection of Channel entities against business rules.
    /// </summary>
    /// <param name="channels">The Channel collection to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateChannels(
        IEnumerable<Channel> channels)
    {
        EntityValidator.ValidateEntityUniqueness(channels);

        foreach (Channel channel in channels)
        {
            ValidateChannel(channel);
        }
    }

    public static void ValidateChannel(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        ArgumentException.ThrowIfNullOrWhiteSpace(channel.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel.Classification);
    }
}
