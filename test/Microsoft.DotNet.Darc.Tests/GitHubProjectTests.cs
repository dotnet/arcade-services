// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Shouldly;
using Microsoft.DotNet.DarcLib;

namespace Microsoft.DotNet.DarcLib.Tests
{
    public class GitHubProjectTests
    {
        [Fact]
        public void TestMethod1()
        {
            var result = new List<int> { 1, 2, 3 };
            result.ShouldNotBeNull();
            result.ShouldContain(2);
        }

        [Fact]
        public void TestMethod2()
        {
            var value = "Hello, World!";
            value.ShouldNotBeNullOrEmpty();
            value.ShouldBe("Hello, World!");
        }
    }
}