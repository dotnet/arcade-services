// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public class WorkBranchException : Exception
{
    public WorkBranchException(string message) : base(message)
    {

    }
}

