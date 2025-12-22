// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.MaestroConfiguration.Client;

/// <summary>
/// Exception thrown when attempting to add a configuration object that already exists.
/// </summary>
public class DuplicateConfigurationObjectException : Exception
{
    public string FilePath { get; }

    public DuplicateConfigurationObjectException(string filePath)
        : base()
    {
        FilePath = filePath;
    }
}
