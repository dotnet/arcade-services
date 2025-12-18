// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Maestro.DataProviders.ConfigurationIngestion.Helpers;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Validations;

internal class ChannelValidator
{
    /// <summary>
    /// Validates a collection of Channel entities against business rules.
    /// </summary>
    /// <param name="channels">The Channel collection to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateChannels(
        IReadOnlyCollection<IngestedChannel> channels)
    {
        EntityValidator.ValidateEntityUniqueness(channels);

        foreach (var channel in channels)
        {
            ValidateChannel(channel);
        }
    }

    public static void ValidateChannel(IngestedChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        ArgumentException.ThrowIfNullOrWhiteSpace(channel.Values.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel.Values.Classification);
    }
}
