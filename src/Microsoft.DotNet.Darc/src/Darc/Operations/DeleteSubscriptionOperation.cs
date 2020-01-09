// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.Maestro.Client.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Maestro.Client;

namespace Microsoft.DotNet.Darc.Operations
{
    internal class DeleteSubscriptionOperation : Operation
    {
        DeleteSubscriptionCommandLineOptions _options;
        public DeleteSubscriptionOperation(DeleteSubscriptionCommandLineOptions options)
            : base(options)
        {
            _options = options;
        }

        public override Task<int> ExecuteAsync()
        {
            Console.WriteLine("The 'delete-subscription' command has been removed. Please use 'delete-subscriptions' instead");
            return Task.FromResult(Constants.ErrorCode);
        }
    }
}
