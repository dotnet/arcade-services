// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace Maestro.Web.Tests
{
    public class NeverBackgroundQueue : IBackgroundQueue
    {
        private readonly ITestOutputHelper _output;

        public NeverBackgroundQueue(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Post<T>(JToken args) where T : IBackgroundWorkItem
        {
            _output.WriteLine($"Denying background call: {typeof(T).Name}({args.ToString(Formatting.None)})");
        }
    }
}
