// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace Maestro.Web.Tests
{
    public class ImmediateBackgroundQueue : IBackgroundQueue
    {
        private readonly IServiceProvider _services;

        public ImmediateBackgroundQueue(IServiceProvider services)
        {
            _services = services;
        }

        public void Post<T>(JToken args) where T : IBackgroundWorkItem
        {
            ActivatorUtilities.CreateInstance<T>(_services).ProcessAsync(args).GetAwaiter().GetResult();
        }
    }
}
