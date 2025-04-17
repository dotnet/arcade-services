// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Shouldly;

namespace DependencyUpdater.Tests
{
    public class NonBatchablePullRequestUpdaterTests
    {
        [Fact]
        public void TestMethod1()
        {
            // Arrange
            var expectedValue = 42;

            // Act
            var actualValue = 42;

            // Assert
            actualValue.ShouldBe(expectedValue);
        }
    }
}