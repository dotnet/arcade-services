// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class EmptySyncException : Exception
{
    public EmptySyncException(string message) : base(message)
    {
    }
}
