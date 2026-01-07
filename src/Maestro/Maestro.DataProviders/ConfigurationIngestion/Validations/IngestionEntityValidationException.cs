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
internal class IngestionEntityValidationException : ArgumentException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IngestionEntityValidationException"/> class.
    /// </summary>
    /// <param name="message">The error message describing the validation failure.</param>
    /// <param name="entity">The entity that failed validation.</param>
    public IngestionEntityValidationException(string message, object entity)
        : base(FormatMessage(message, entity))
    {
        EntityInfo = entity.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Gets information about the entity that failed validation.
    /// </summary>
    public string EntityInfo { get; }

    private static string FormatMessage(string message, object entity)
    {
        return $"{message} Entity: {entity}";
    }
}
