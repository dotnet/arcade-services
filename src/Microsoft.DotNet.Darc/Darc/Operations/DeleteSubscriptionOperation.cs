// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Options;
using System;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations;

internal class DeleteSubscriptionOperation(DeleteSubscriptionCommandLineOptions options)
    : Operation(options)
{
    public override Task<int> ExecuteAsync()
    {
        Console.WriteLine("The 'delete-subscription' command has been removed. Please use 'delete-subscriptions' instead");
        return Task.FromResult(Constants.ErrorCode);
    }
}
