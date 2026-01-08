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

        if (string.IsNullOrWhiteSpace(defaultChannel._values.Repository))
        {
            throw new IngestionEntityValidationException("Default channel repository is required.", defaultChannel);
        }

        if (string.IsNullOrWhiteSpace(defaultChannel._values.Branch))
        {
            throw new IngestionEntityValidationException("Default channel branch is required.", defaultChannel);
        }

        if (defaultChannel._values.Repository.Length > 300)
        {
            throw new IngestionEntityValidationException("Default channel repository cannot be longer than 300 characters.", defaultChannel);
        }

        if (defaultChannel._values.Branch.Length > 100)
        {
            throw new IngestionEntityValidationException("Default channel branch name cannot be longer than 100 characters.", defaultChannel);
        }
    }
}
