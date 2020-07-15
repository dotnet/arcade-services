// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Maestro.Web.Tests
{
    public class NeverBackgroundQueue : IBackgroundQueue
    {
        public void Post<T>(JToken args) where T : IBackgroundWorkItem
        {
            TestContext.WriteLine($"Denying background call: {typeof(T).Name}({args.ToString(Formatting.None)})");
        }
    }
}
