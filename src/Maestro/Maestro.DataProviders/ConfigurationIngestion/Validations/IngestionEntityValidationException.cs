// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

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
    /// <param name="entityInfo">A string representation of the entity that failed validation.</param>
    public IngestionEntityValidationException(string message, string entityInfo)
        : base(FormatMessage(message, entityInfo))
    {
        EntityInfo = entityInfo;
    }

    /// <summary>
    /// Gets information about the entity that failed validation.
    /// </summary>
    public string EntityInfo { get; }

    private static string FormatMessage(string message, string entityInfo)
    {
        return $"{message} Entity: {entityInfo}";
    }
}
