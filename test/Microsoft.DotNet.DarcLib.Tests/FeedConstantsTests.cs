// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Shouldly;

namespace MyNamespace.Tests
{
    public class FeedConstantsTests
    {
        [Fact]
        public void SomeTest()
        {
            var expected = "expectedValue";
            var actual = "actualValue";

            actual.ShouldBe(expected);
        }
    }
}