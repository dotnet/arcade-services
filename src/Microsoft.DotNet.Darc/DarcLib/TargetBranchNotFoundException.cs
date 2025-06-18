// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Microsoft.DotNet.DarcLib;

public class TargetBranchNotFoundException : DarcException
{
    public TargetBranchNotFoundException() : base()
    {
    }

    public TargetBranchNotFoundException(string message) : base(message)
    {
    }

    public TargetBranchNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

