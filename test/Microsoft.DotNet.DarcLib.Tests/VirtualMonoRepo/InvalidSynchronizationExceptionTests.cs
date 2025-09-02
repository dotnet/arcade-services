// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;
using System;
using System.Collections.Generic;


namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class InvalidSynchronizationExceptionTests
{
    /// <summary>
    /// Ensures that constructing InvalidSynchronizationException with various valid messages:
    ///  - preserves the message exactly as provided (including empty, whitespace, long, multiline, special/Unicode/control characters),
    ///  - and does not set an inner exception.
    /// Inputs:
    ///  - message: non-null string values covering typical and edge cases.
    /// Expected:
    ///  - The created exception has the same Message and a null InnerException.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ValidMessages))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_Message_AssignedToExceptionMessageAndNoInnerException(string message)
    {
        // Arrange
        // message is provided by TestCaseSource and is non-null by contract.

        // Act
        var exception = new InvalidSynchronizationException(message);

        // Assert
        exception.Should().BeOfType<InvalidSynchronizationException>();
        exception.Message.Should().Be(message);
        exception.InnerException.Should().BeNull();
    }

    private static IEnumerable<string> ValidMessages()
    {
        yield return "A simple error message";
        yield return string.Empty;
        yield return "   ";
        yield return new string('a', 10000);
        yield return "First line\nSecond line\r\nThird line";
        yield return "Emoji 😀 🚀; specials !@#$%^&*()_+-=[]{}|;':\",.<>/?`~";
        yield return "Controls:\0 \u0001 \u001F End";
        yield return "Unicode: Привет こんにちは مرحبا";
    }
}
