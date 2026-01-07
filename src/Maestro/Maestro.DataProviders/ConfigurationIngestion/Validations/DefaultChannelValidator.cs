// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Maestro.DataProviders.ConfigurationIngestion.Model;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Validations;

internal class DefaultChannelValidator
{
    /// <summary>
    /// Validates a collection of DefaultChannels entities against business rules.
    /// </summary>
    /// <param name="defaultChannels">The DefaultChannel collection to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    internal static void ValidateDefaultChannels(
        IReadOnlyCollection<IngestedDefaultChannel> defaultChannels)
    {
        EntityValidator.ValidateEntityUniqueness(defaultChannels);

        foreach (var defaultChannel in defaultChannels)
        {
            ValidateDefaultChannel(defaultChannel);
        }
    }

    internal static void ValidateDefaultChannel(IngestedDefaultChannel defaultChannel)
    {
        ArgumentNullException.ThrowIfNull(defaultChannel);

        if (string.IsNullOrWhiteSpace(defaultChannel.Values.Repository))
        {
            throw new IngestionEntityValidationException("Default channel repository is required.", defaultChannel.ToString());
        }

        if (string.IsNullOrWhiteSpace(defaultChannel.Values.Branch))
        {
            throw new IngestionEntityValidationException("Default channel branch is required.", defaultChannel.ToString());
        }

        if (defaultChannel.Values.Repository.Length > 300)
        {
            throw new IngestionEntityValidationException("Default channel repository cannot be longer than 300 characters.", defaultChannel.ToString());
        }

        if (defaultChannel.Values.Branch.Length > 100)
        {
            throw new IngestionEntityValidationException("Default channel branch name cannot be longer than 100 characters.", defaultChannel.ToString());
        }
    }
}
