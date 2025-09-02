// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;
using System;
using System.Collections.Generic;


namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class EmptySyncExceptionTests
{
    private static readonly string VeryLongMessage = new string('a', 10000);

    private static IEnumerable<string> MessageCases()
    {
        yield return "";
        yield return " ";
        yield return "  \t  ";
        yield return "Normal message.";
        yield return "🚀🔥 Unicode message 漢字";
        yield return "Line1\nLine2\r\nLine3\tTabbed\u0001Control";
        yield return VeryLongMessage;
    }

    /// <summary>
    /// Ensures the constructor assigns the provided message to the Message property and no InnerException is set.
    /// Inputs:
    ///  - message: empty, whitespace, normal text, unicode/special characters, control characters, and a very long string.
    /// Expected:
    ///  - The created EmptySyncException is not null.
    ///  - Message equals the provided input string.
    ///  - InnerException is null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(MessageCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void EmptySyncException_Message_SetsMessageAndNoInnerException(string message)
    {
        // Arrange

        // Act
        var exception = new EmptySyncException(message);

        // Assert
        exception.Should().NotBeNull();
        exception.Message.Should().Be(message);
        exception.InnerException.Should().BeNull();
    }
}
