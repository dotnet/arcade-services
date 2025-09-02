// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime;
using System.Runtime.Serialization;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.UnitTests;

public class DependencyExceptionTests
{
    /// <summary>
    /// Validates that the parameterless constructor creates a DependencyException instance
    /// without an inner exception and with a non-null message.
    /// Inputs:
    ///  - None (parameterless constructor).
    /// Expected:
    ///  - Instance is created successfully.
    ///  - InnerException is null.
    ///  - Message is not null.
    ///  - Object is assignable to DarcException.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DependencyException_ParameterlessConstructor_InitializesWithoutInnerException()
    {
        // Arrange
        // No arrangement needed for default constructor.

        // Act
        var exception = new DependencyException();

        // Assert
        exception.Should().NotBeNull();
        exception.Should().BeAssignableTo<DarcException>();
        exception.InnerException.Should().BeNull();
        exception.Message.Should().NotBeNull();
    }

    private sealed class DependencyExceptionProxy : DependencyException
    {
        public DependencyExceptionProxy(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    private static IEnumerable<TestCaseData> ValidInfoCases()
    {
        yield return new TestCaseData("basic message", StreamingContextStates.All);
        yield return new TestCaseData("whitespace \t and \n newlines Ω", StreamingContextStates.CrossProcess);
        yield return new TestCaseData(new string('x', 512), StreamingContextStates.File);
    }

    /// <summary>
    /// Ensures that constructing with an empty SerializationInfo results in a SerializationException.
    /// Inputs:
    ///  - An empty SerializationInfo (no keys populated).
    ///  - A default StreamingContext.
    /// Expected:
    ///  - The protected constructor throws a SerializationException due to missing required serialization entries.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithEmptySerializationInfo_ThrowsSerializationException()
    {
        // Arrange
        var info = new SerializationInfo(typeof(DependencyException), new FormatterConverter());
        var context = new StreamingContext(StreamingContextStates.Other);

        // Act
        Action act = () => new DependencyExceptionProxy(info, context);

        // Assert
        act.Should().Throw<SerializationException>();
    }

    private static IEnumerable<string> ValidMessages()
    {
        yield return "A simple message";
        yield return string.Empty;
        yield return " ";
        yield return "\t\r\n";
        yield return "Special chars: ~!@#$%^&*()_+{}|:\"<>?-=[]\\;',./`";
        yield return "Unicode: Привет мир 🌟";
        yield return new string('x', 4096);
    }

    /// <summary>
    /// Verifies that the DependencyException(string message) constructor sets the Message property
    /// to the provided input.
    /// Inputs:
    ///  - message: diverse non-null strings including empty, whitespace, special characters, unicode, and very long text.
    /// Expected:
    ///  - The constructed exception's Message equals the provided message.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ValidMessages))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DependencyException_WithMessage_SetsMessage(string message)
    {
        // Arrange

        // Act
        var exception = new DependencyException(message);

        // Assert
        exception.Should().NotBeNull();
        exception.Message.Should().Be(message);
    }

    /// <summary>
    /// Verifies that the DependencyException(string message) constructor handles a null message
    /// by producing a non-null, non-empty default message (as provided by the base Exception type).
    /// Inputs:
    ///  - message: null
    /// Expected:
    ///  - The constructed exception's Message is not null, empty, or whitespace.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DependencyException_NullMessage_DefaultMessageIsUsed()
    {
        // Arrange
        string message = null;

        // Act
        var exception = new DependencyException(message);

        // Assert
        exception.Should().NotBeNull();
        exception.Message.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Validates that the (string message, Exception innerException) constructor:
    /// - Assigns the provided Message when non-null.
    /// - Preserves the InnerException reference (including when null).
    /// - Produces an instance assignable to both DependencyException and DarcException.
    /// Inputs:
    ///  - Various message strings including null, empty, whitespace, long, and special characters.
    ///  - Various inner exceptions including null and different exception types.
    /// Expected:
    ///  - For non-null message: ex.Message equals the provided string exactly.
    ///  - For null message: ex.Message is not null (default behavior from base Exception).
    ///  - ex.InnerException equals the provided inner exception reference (or null, accordingly).
    ///  - ex is assignable to DarcException.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(Ctor_WithMessageAndInnerException_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void DependencyException_Ctor_WithVariousInputs_SetsMessageAndInnerException(string message, Exception inner)
    {
        // Arrange
        // (Inputs provided by TestCaseSource)

        // Act
        var ex = new DependencyException(message, inner);

        // Assert
        ex.Should().BeAssignableTo<DarcException>();
        ex.Should().BeOfType<DependencyException>();

        if (message != null)
        {
            ex.Message.Should().Be(message);
        }
        else
        {
            ex.Message.Should().NotBeNull();
        }

        if (inner is null)
        {
            ex.InnerException.Should().BeNull();
        }
        else
        {
            ex.InnerException.Should().BeSameAs(inner);
        }
    }

    private static IEnumerable<TestCaseData> Ctor_WithMessageAndInnerException_Cases()
    {
        yield return new TestCaseData("simple message", new Exception("inner"))
            .SetName("Ctor_MessageAndInnerException_SetsBoth");
        yield return new TestCaseData(string.Empty, null)
            .SetName("Ctor_EmptyMessage_NullInner_PreservesValues");
        yield return new TestCaseData("   ", new InvalidOperationException("op"))
            .SetName("Ctor_WhitespaceMessage_WithInvalidOperationException_PreservesValues");
        yield return new TestCaseData(new string('x', 4096), new ArgumentException("arg", "param"))
            .SetName("Ctor_VeryLongMessage_WithArgumentException_PreservesValues");
        yield return new TestCaseData("\t\r\n Special ☃️ chars", new ApplicationException("app"))
            .SetName("Ctor_SpecialCharactersMessage_WithApplicationException_PreservesValues");
        yield return new TestCaseData(null, new NullReferenceException("nre"))
            .SetName("Ctor_NullMessage_WithInnerException_MessageDefaultsAndInnerPreserved");
        yield return new TestCaseData(null, null)
            .SetName("Ctor_NullMessage_NullInner_MessageDefaultsAndInnerNull");
    }
}
