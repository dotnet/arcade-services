// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

public abstract class Operation
{
    public abstract Task<int> ExecuteAsync();
}
