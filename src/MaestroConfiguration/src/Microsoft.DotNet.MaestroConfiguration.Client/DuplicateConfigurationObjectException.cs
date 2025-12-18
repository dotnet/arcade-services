// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

/// <summary>
/// Exception thrown when attempting to add a configuration object that already exists.
/// </summary>
public class DuplicateConfigurationObjectException : Exception
{
    /// <summary>
    /// Gets the file path where the duplicate was found.
    /// </summary>
    public string FilePath { get; }

    public DuplicateConfigurationObjectException(string message, string filePath)
        : base(message)
    {
        FilePath = filePath;
    }

    public DuplicateConfigurationObjectException(string message, string filePath, Exception innerException)
        : base(message, innerException)
    {
        FilePath = filePath;
    }
}
