// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Maestro.Web.Tests
{
    public class ImmediateBackgroundQueue : IBackgroundQueue
    {
        private readonly IServiceProvider _services;
        private readonly ITestOutputHelper _output;

        public ImmediateBackgroundQueue(
            IServiceProvider services,
            ITestOutputHelper output)
        {
            _services = services;
            _output = output;
        }

        public void Post<T>(JToken args) where T : IBackgroundWorkItem
        {
            _output.WriteLine($"Immediate background call: {typeof(T).Name}({args.ToString(Formatting.None)})");
            ActivatorUtilities.CreateInstance<T>(_services).ProcessAsync(args).GetAwaiter().GetResult();
        }
    }
}
