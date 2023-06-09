// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace Microsoft.DotNet.DarcLib;

[Serializable]
public class DarcException : Exception
{
    public DarcException()
    {
    }

    protected DarcException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }

    public DarcException(string message) : base(message)
    {
    }

    public DarcException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
