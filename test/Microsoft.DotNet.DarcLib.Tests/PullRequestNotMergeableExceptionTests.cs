// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.UnitTests;

public class PullRequestNotMergeableExceptionTests
{
    /// <summary>
    /// Ensures that the parameterless constructor creates an exception instance with a non-empty default message
    /// and no inner exception.
    /// Inputs:
    ///  - No parameters.
    /// Expected:
    ///  - Instance is created successfully.
    ///  - Message is not null or whitespace.
    ///  - InnerException is null.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void PullRequestNotMergeableException_NoArgs_DefaultMessageAndNoInnerException()
    {
        // Arrange
        // No setup required.

        // Act
        var ex = new PullRequestNotMergeableException();

        // Assert
        ex.Should().BeOfType<PullRequestNotMergeableException>();
        ex.Should().BeAssignableTo<DarcException>();
        ex.Message.Should().NotBeNullOrWhiteSpace();
        ex.InnerException.Should().BeNull();
    }

    /// <summary>
    /// Validates that the constructor with a message preserves the provided message across a range of inputs
    /// (empty, whitespace, control characters, Unicode, special characters, and very long strings), does not set
    /// an inner exception, and produces the correct exception type which is also assignable to DarcException.
    /// Inputs:
    ///  - Various non-null strings from <see cref="MessageTestCases"/>.
    /// Expected:
    ///  - exception.Message equals the provided input.
    ///  - exception.InnerException is null.
    ///  - exception is of type PullRequestNotMergeableException and assignable to DarcException.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MessageTestCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_Message_SetsMessageAndNoInnerException(string message)
    {
        // Arrange
        // message provided by TestCaseSource

        // Act
        var exception = new PullRequestNotMergeableException(message);

        // Assert
        exception.Should().BeOfType<PullRequestNotMergeableException>();
        exception.Should().BeAssignableTo<DarcException>();
        exception.Message.Should().Be(message);
        exception.InnerException.Should().BeNull();
    }

    private static object[] MessageTestCases() => new object[]
    {
            "",
            " ",
            "A concise error message.",
            "\t \n \r",
            "こんにちは世界",
            "SpecialChars: !@#$%^&*()_+-={}[]|\\:'\",.<>/?`~",
            new string('x', 1024),
            "ContainsNullChar:\0:End"
    };
}
