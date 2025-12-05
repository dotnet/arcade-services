// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Maestro.DataProviders.Exceptions;

public class EntityConflictException : Exception
{
    public EntityConflictException() { }

    public EntityConflictException(string message)
        : base(message) { }

    public EntityConflictException(string message, Exception innerException)
        : base(message, innerException) { }
}
