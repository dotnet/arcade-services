// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.Serialization;
using FluentAssertions;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

public class DarcAuthenticationFailureExceptionTests
{
    /// <summary>
    /// Validates that the parameterless constructor creates a valid instance without an inner exception.
    /// Inputs:
    ///  - No inputs (default constructor).
    /// Expected:
    ///  - Instance is created successfully.
    ///  - InnerException is null.
    ///  - Message is not null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void DarcAuthenticationFailureException_DefaultCtor_InitializesInstance()
    {
        // Arrange
        // No arrangement needed for default constructor.

        // Act
        var exception = new DarcAuthenticationFailureException();

        // Assert
        exception.Should().NotBeNull();
        exception.Should().BeOfType<DarcAuthenticationFailureException>();
        exception.InnerException.Should().BeNull();
        exception.Message.Should().NotBeNull();
    }

    /// <summary>
    /// Ensures that an instance constructed with the default constructor can populate serialization data.
    /// Inputs:
    ///  - No inputs (default constructor).
    /// Expected:
    ///  - Calling GetObjectData succeeds and writes members into SerializationInfo without throwing.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void DarcAuthenticationFailureException_DefaultCtor_SerializesWithGetObjectData()
    {
        // Arrange
        var exception = new DarcAuthenticationFailureException();
        var info = new SerializationInfo(typeof(DarcAuthenticationFailureException), new FormatterConverter());
        var context = new StreamingContext(StreamingContextStates.All);

        // Act
        exception.GetObjectData(info, context);

        // Assert
        info.MemberCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Validates that the message-only constructor correctly initializes the exception.
    /// Inputs:
    ///  - Various non-null message strings, including empty, whitespace-only, long, control characters, and Unicode.
    /// Expected:
    ///  - The created instance is of type DarcAuthenticationFailureException and assignable to DarcException.
    ///  - The Message property equals the provided message exactly.
    ///  - The InnerException is null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(MessageCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithVariousMessages_MessageSetAndInnerExceptionNull(string message)
    {
        // Arrange
        // message is provided by TestCaseSource

        // Act
        var exception = new DarcAuthenticationFailureException(message);

        // Assert
        exception.Should().BeOfType<DarcAuthenticationFailureException>();
        exception.Should().BeAssignableTo<DarcException>();
        exception.Message.Should().Be(message);
        exception.InnerException.Should().BeNull();
    }

    private static System.Collections.Generic.IEnumerable<string> MessageCases()
    {
        yield return "simple message";
        yield return string.Empty;
        yield return "   ";
        yield return new string('x', 10_000);
        yield return "line1\nline2\twith\0control";
        yield return "Path: C:\\temp\\file.txt and quote: \"value\"";
        yield return "Unicode: Δοκιμή / 测试 / 🌟✨";
    }

    /// <summary>
    /// Provides diverse message and inner-exception combinations to validate that the two-parameter constructor
    /// correctly assigns the Message and InnerException properties without alteration.
    /// Includes edge cases for message content (empty, whitespace, special/control characters, very long string).
    /// </summary>
    public static IEnumerable<TestCaseData> MessageAndInnerExceptionCases()
    {
        yield return new TestCaseData(
            "Authentication failed",
            new Exception("root cause"))
            .SetName("Constructor_MessageAndInnerException_AssignsProperties_Normal");

        yield return new TestCaseData(
            "",
            new InvalidOperationException("operation failed"))
            .SetName("Constructor_MessageAndInnerException_AssignsProperties_EmptyMessage");

        yield return new TestCaseData(
            " \t ",
            new ArgumentException("bad arg"))
            .SetName("Constructor_MessageAndInnerException_AssignsProperties_WhitespaceMessage");

        yield return new TestCaseData(
            "line1\r\nline2\tunicode☃ and control:\0end",
            new ApplicationException("app error"))
            .SetName("Constructor_MessageAndInnerException_AssignsProperties_SpecialAndControlChars");

        yield return new TestCaseData(
            new string('a', 10_000),
            new NullReferenceException("nre"))
            .SetName("Constructor_MessageAndInnerException_AssignsProperties_VeryLongMessage");
    }

    /// <summary>
    /// Ensures that the two-argument constructor sets the Message to the provided string and the InnerException
    /// to the exact instance supplied.
    /// Inputs:
    ///  - message: non-null strings including empty, whitespace-only, special/control characters, and very long text.
    ///  - innerException: a non-null Exception instance of various types.
    /// Expected:
    ///  - The constructed exception's Message equals the input message.
    ///  - The constructed exception's InnerException is the same instance as the input innerException.
    ///  - The constructed exception is assignable to DarcException and System.Exception.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(MessageAndInnerExceptionCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_MessageAndInnerException_AssignsProperties(string message, Exception innerException)
    {
        // Arrange
        // (inputs provided by TestCaseSource)

        // Act
        var ex = new DarcAuthenticationFailureException(message, innerException);

        // Assert
        ex.Should().NotBeNull();
        ex.Message.Should().Be(message);
        ex.InnerException.Should().BeSameAs(innerException);
        ex.Should().BeAssignableTo<DarcException>();
        ex.Should().BeAssignableTo<Exception>();
    }
}
