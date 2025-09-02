// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.UnitTests;


public class TargetBranchNotFoundExceptionTests
{
    /// <summary>
    /// Ensures the parameterless constructor creates a valid exception instance.
    /// Inputs:
    ///  - No inputs (default constructor).
    /// Expected:
    ///  - Instance is created successfully.
    ///  - InnerException is null.
    ///  - Message is not null.
    ///  - Instance is assignable to DarcException.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_Default_InitializesWithNullInnerExceptionAndNonNullMessage()
    {
        // Arrange

        // Act
        var exception = new TargetBranchNotFoundException();

        // Assert
        exception.Should().NotBeNull();
        exception.Should().BeAssignableTo<DarcException>();
        exception.InnerException.Should().BeNull();
        exception.Message.Should().NotBeNull();
    }

    /// <summary>
    /// Validates that the string-message constructor correctly propagates the provided message to the exception's Message property
    /// and does not set an InnerException.
    /// Inputs:
    ///  - message: Various string inputs including null, empty, whitespace, long, and special-character strings.
    /// Expected:
    ///  - For non-null message: exception.Message equals the provided message.
    ///  - For null message: exception.Message is not null or empty (default framework behavior).
    ///  - InnerException is null in all cases.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MessageCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithVariousMessages_SetsMessageAndNoInnerException(string message)
    {
        // Arrange
        // message provided by test case source

        // Act
        var exception = new TargetBranchNotFoundException(message);

        // Assert
        if (message == null)
        {
            exception.Message.Should().NotBeNullOrEmpty();
        }
        else
        {
            exception.Message.Should().Be(message);
        }

        exception.InnerException.Should().BeNull();
        exception.Should().BeOfType<TargetBranchNotFoundException>();
    }

    private static System.Collections.Generic.IEnumerable<string> MessageCases()
    {
        yield return null;
        yield return string.Empty;
        yield return " ";
        yield return new string('a', 2048);
        yield return "Line1\nLine2\t\u0000 NullChar and emojis 😀🚀";
        yield return "Target branch 'main' not found.";
    }

    /// <summary>
    /// Validates that the (string message, Exception innerException) constructor correctly assigns
    /// the Message and InnerException properties for various message edge cases and inner exception types.
    /// Inputs:
    ///  - message: empty, whitespace, normal, very long, and special-character strings.
    ///  - innerException: different concrete Exception types.
    /// Expected:
    ///  - The created exception is non-null, Message equals the provided message,
    ///    and InnerException references the same instance as provided.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(MessageAndInnerExceptionCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_MessageAndInnerException_PreservesValues(string message, Exception innerException)
    {
        // Arrange
        // (Inputs provided by TestCaseSource)

        // Act
        var exception = new TargetBranchNotFoundException(message, innerException);

        // Assert
        exception.Should().NotBeNull();
        exception.Message.Should().Be(message);
        exception.InnerException.Should().BeSameAs(innerException);
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> MessageAndInnerExceptionCases()
    {
        yield return new TestCaseData("A clear message.", new InvalidOperationException("Invalid operation inner."));
        yield return new TestCaseData(string.Empty, new ArgumentException("Invalid arg", "param"));
        yield return new TestCaseData("   ", new Exception("Base inner."));
        yield return new TestCaseData("Ω≈ç√∫˜µ≤≥÷\r\n\t\u0000 emoji 😀", new ApplicationException("Application inner."));
        yield return new TestCaseData(new string('x', 5000), new NullReferenceException("Null ref inner."));
    }

    /// <summary>
    /// Ensures the parameterless constructor creates a valid exception instance.
    /// Inputs:
    ///  - No inputs (default constructor).
    /// Expected:
    ///  - Instance is created successfully.
    ///  - InnerException is null.
    ///  - Message is not null or empty (framework default message).
    ///  - Instance is assignable to DarcException.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_Default_InitializesWithExpectedDefaults()
    {
        // Arrange
        // No arrangement needed for parameterless constructor.

        // Act
        var exception = new TargetBranchNotFoundException();

        // Assert
        exception.Should().NotBeNull();
        exception.InnerException.Should().BeNull();
        exception.Message.Should().NotBeNullOrEmpty();
        exception.Should().BeAssignableTo<DarcException>();
    }

}
