// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.DarcLib;

public class DarcException : Exception
{
    public DarcException()
    {
    }

    public DarcException(string message) : base(message)
    {
    }

    public DarcException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
