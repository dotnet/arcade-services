// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Maestro.Data.Models;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestor.Validations;

public class DefaultChannelValidator
{
    /// <summary>
    /// Validates a collection of DefaultChannels entities against business rules.
    /// </summary>
    /// <param name="defaultChannels">The DefaultChannel collection to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateDefaultChannels(
        IEnumerable<DefaultChannel> defaultChannels)
    {
        EntityValidator.ValidateEntityUniqueness(defaultChannels);

        foreach (DefaultChannel defaultChannel in defaultChannels)
        {
            ValidateDefaultChannel(defaultChannel);
        }
    }

    public static void ValidateDefaultChannel(DefaultChannel defaultChannel)
    {
        ArgumentNullException.ThrowIfNull(defaultChannel);

        ArgumentException.ThrowIfNullOrWhiteSpace(defaultChannel.Repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultChannel.Branch);

        if (defaultChannel.Repository.Length > 300)
        {
            throw new ArgumentException("Default channel repository cannot be longer than 300 characters.");
        }

        if (defaultChannel.Branch.Length > 100)
        {
            throw new ArgumentException("Default channel branch name cannot be longer than 100 characters.");
        }
    }
}
