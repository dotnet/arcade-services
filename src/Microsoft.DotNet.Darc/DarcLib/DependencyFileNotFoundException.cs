// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.DarcLib;

public class DependencyFileNotFoundException : DarcException
{
    public string File { get; set; }

    public DependencyFileNotFoundException(string filePath, string repository, string branch, Exception innerException)
        : base($"Required dependency file '{filePath}' in repository '{repository}' branch '{branch}' was not found.", innerException)
    {
        File = filePath;
    }

    public DependencyFileNotFoundException()
    {
    }

    public DependencyFileNotFoundException(string message) : base(message)
    {
    }

    public DependencyFileNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
