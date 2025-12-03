// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Maestro.Data.Models;

namespace Maestro.DataProviders.ConfigurationIngestor.Validations;

public class ChannelValidator
{
    /// <summary>
    /// Validates a Channel entity against business rules.
    /// </summary>
    /// <param name="channel">The Channel to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateChannel(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        ArgumentException.ThrowIfNullOrWhiteSpace(channel.Name, nameof(channel.Name));
        ArgumentException.ThrowIfNullOrWhiteSpace(channel.Classification, nameof(channel.Classification));
    }
}
