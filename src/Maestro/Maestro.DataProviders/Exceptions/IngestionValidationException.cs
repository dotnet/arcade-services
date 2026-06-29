// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Maestro.DataProviders.Exceptions;

/// <summary>
/// Base exception for all ingestion exceptions that are due to bad configuration data.
/// </summary>
public class IngestionValidationException : Exception
{
    public IngestionValidationException() { }
    public IngestionValidationException(string message)
        : base(message) { }
    public IngestionValidationException(string message, Exception innerException)
        : base(message, innerException) { }
}
