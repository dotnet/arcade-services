// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.Serialization;

namespace Microsoft.DotNet.DarcLib;

[Serializable]
public class DependencyException : DarcException
{
    public DependencyException() : base()
    {
    }

    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.")]
    protected DependencyException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }

    public DependencyException(string message) : base(message)
    {
    }

    public DependencyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
