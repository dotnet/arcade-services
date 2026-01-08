// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Maestro.DataProviders.ConfigurationIngestion.Model;

#nullable enable
namespace Maestro.DataProviders.ConfigurationIngestion.Validations;

/// <summary>
/// Exception thrown when validation of an ingestion entity fails.
/// Includes information about the entity that failed validation.
/// </summary>
public class IngestionEntityValidationException : Exception
{
    public string EntityInfo { get; }

    public IngestionEntityValidationException(string message)
        : base(message)
    {
        EntityInfo = "";
    }

    public IngestionEntityValidationException(string message, IExternallySyncedEntity entity)
        : base(FormatMessage(message, entity))
    {
        EntityInfo = entity.ToString() ?? string.Empty;
    }

    private static string FormatMessage(string message, IExternallySyncedEntity entity)
    {
        return $"Validation failed for entity {entity.ToString()} for the following reason: {message}";
    }
}
