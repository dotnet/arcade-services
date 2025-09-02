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

namespace Microsoft.DotNet.DarcLib.UnitTests;


public class GithubApplicationInstallationExceptionTests
{
    private sealed class ExceptionCtorAccessor : GithubApplicationInstallationException
    {
        public ExceptionCtorAccessor(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    private static IEnumerable<string> LongMessages()
    {
        yield return new string('a', 5000);
    }

    /// <summary>
    /// Verifies that the parameterless constructor creates a valid exception instance
    /// with a non-empty default message and no inner exception.
    /// Inputs:
    ///  - None (default constructor).
    /// Expected:
    ///  - Instance is created.
    ///  - Message is not null or empty.
    ///  - InnerException is null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Ctor_Default_InitializesWithNonEmptyMessageAndNoInnerException()
    {
        // Arrange

        // Act
        var ex = new GithubApplicationInstallationException();

        // Assert
        ex.Should().NotBeNull();
        ex.InnerException.Should().BeNull();
        ex.Message.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Validates that the protected serialization constructor throws when provided SerializationInfo
    /// lacks the required Exception fields.
    /// Inputs:
    ///  - An empty SerializationInfo with no keys populated.
    /// Expected:
    ///  - A SerializationException is thrown by the base Exception deserialization logic.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithEmptySerializationInfo_ThrowsSerializationException()
    {
        // Arrange
        var info = new SerializationInfo(typeof(GithubApplicationInstallationException), new FormatterConverter());
        var context = new StreamingContext(StreamingContextStates.All);

        // Act
        try
        {
            var _ = new ExceptionCtorAccessor(info, context);

            // Assert
            // Reaching this line means no exception was thrown, which is unexpected.
            throw new Exception("Expected SerializationException was not thrown by the serialization constructor.");
        }
        catch (SerializationException)
        {
            // Expected path: the Exception base deserialization detects missing fields.
        }
    }

    /// <summary>
    /// Ensures that when SerializationInfo is properly populated using GetObjectData from a source exception,
    /// the protected serialization constructor restores the Message and InnerException as expected.
    /// Inputs:
    ///  - A source GithubApplicationInstallationException with a given message and optional inner exception.
    ///  - SerializationInfo populated via GetObjectData.
    /// Expected:
    ///  - Deserialization completes without throwing.
    ///  - The resulting exception has the same Message and presence/absence of InnerException as the source.
    /// </summary>
    [TestCaseSource(nameof(PopulatedInfoCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_WithPopulatedSerializationInfo_RestoresMessageAndInner(string message, bool includeInnerException, string expectedInnerMessage)
    {
        // Arrange
        var context = new StreamingContext(StreamingContextStates.All);
        var info = new SerializationInfo(typeof(GithubApplicationInstallationException), new FormatterConverter());

        GithubApplicationInstallationException source;
        if (includeInnerException)
        {
            source = new GithubApplicationInstallationException(message, new InvalidOperationException(expectedInnerMessage));
        }
        else
        {
            source = new GithubApplicationInstallationException(message);
        }

        source.GetObjectData(info, context);

        // Act
        var deserialized = new ExceptionCtorAccessor(info, context);

        // Assert
        if (!string.Equals(deserialized.Message, message, StringComparison.Ordinal))
        {
            throw new Exception($"Deserialized Message mismatch. Expected: '{message}', Actual: '{deserialized.Message}'.");
        }

        if (includeInnerException)
        {
            if (deserialized.InnerException == null)
            {
                throw new Exception("Expected InnerException to be present after deserialization, but it was null.");
            }

            if (!string.Equals(deserialized.InnerException.Message, expectedInnerMessage, StringComparison.Ordinal))
            {
                throw new Exception($"Deserialized InnerException.Message mismatch. Expected: '{expectedInnerMessage}', Actual: '{deserialized.InnerException.Message}'.");
            }
        }
        else
        {
            if (deserialized.InnerException != null)
            {
                throw new Exception("Did not expect InnerException to be present after deserialization, but it was not null.");
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> PopulatedInfoCases()
    {
        yield return new TestCaseData("", false, "").SetName("Constructor_WithPopulatedSerializationInfo_EmptyMessage_NoInner");
        yield return new TestCaseData(" ", false, "").SetName("Constructor_WithPopulatedSerializationInfo_WhitespaceMessage_NoInner");
        yield return new TestCaseData(new string('a', 5000), true, "inner-error").SetName("Constructor_WithPopulatedSerializationInfo_VeryLongMessage_WithInner");
        yield return new TestCaseData("Special chars: \u0000 \u263A \t \n", true, "special-inner").SetName("Constructor_WithPopulatedSerializationInfo_SpecialChars_WithInner");
    }

    private static IEnumerable<string> MessageCases()
    {
        yield return string.Empty;                  // empty
        yield return " ";                           // whitespace
        yield return "\t";                          // tab
        yield return "line1\nline2";                // multiline
        yield return "special:\u0000\u2603🚀";      // control + unicode + emoji
        yield return new string('a', 5000);         // very long
    }

    /// <summary>
    /// Validates that the message-only constructor assigns the provided message to the exception's Message property
    /// and that InnerException is null.
    /// Inputs:
    ///  - Various message strings: empty, whitespace, multiline, long, and special-character content.
    /// Expected:
    ///  - The constructed exception is not null.
    ///  - Message equals the provided input string.
    ///  - InnerException is null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(MessageCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_Message_SetsMessageAndNullInnerException(string message)
    {
        // Arrange
        // (message provided by TestCaseSource)

        // Act
        var ex = new GithubApplicationInstallationException(message);

        // Assert
        ex.Should().NotBeNull();
        ex.Message.Should().Be(message);
        ex.InnerException.Should().BeNull();
    }

    /// <summary>
    /// Verifies that the (string message, Exception innerException) constructor
    /// preserves the provided non-null message exactly and sets InnerException to the provided instance.
    /// Inputs:
    ///  - Various non-null message strings: simple, empty, whitespace, special/unicode.
    ///  - innerNull flag indicating whether innerException is null.
    /// Expected:
    ///  - ex.Message equals the provided message exactly.
    ///  - ex.InnerException equals the provided instance or is null accordingly.
    /// </summary>
    [TestCase("simple", true)]
    [TestCase("simple", false)]
    [TestCase("", false)]
    [TestCase("   ", true)]
    [TestCase("line1\nline2\tTabbed", false)]
    [TestCase("Text with Üñíçødé 😀", true)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_MessageAndInner_SetsProperties(string message, bool innerNull)
    {
        // Arrange
        Exception inner = innerNull ? null : new InvalidOperationException("inner-error");

        // Act
        var ex = new GithubApplicationInstallationException(message, inner);

        // Assert
        ex.Message.Should().Be(message);
        if (innerNull)
        {
            ex.InnerException.Should().BeNull();
        }
        else
        {
            ex.InnerException.Should().BeSameAs(inner);
        }
    }

    /// <summary>
    /// Verifies that when constructed with a null message, the exception still has a non-empty Message,
    /// and that InnerException is set to the provided value.
    /// Inputs:
    ///  - message = null
    ///  - innerNull flag indicating whether innerException is null.
    /// Expected:
    ///  - ex.Message is not null or empty (default framework message).
    ///  - ex.InnerException equals the provided instance or is null accordingly.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_NullMessage_SetsInnerAndKeepsNonEmptyDefaultMessage(bool innerNull)
    {
        // Arrange
        string message = null;
        Exception inner = innerNull ? null : new Exception("inner");

        // Act
        var ex = new GithubApplicationInstallationException(message, inner);

        // Assert
        ex.Message.Should().NotBeNullOrEmpty();
        if (innerNull)
        {
            ex.InnerException.Should().BeNull();
        }
        else
        {
            ex.InnerException.Should().BeSameAs(inner);
        }
    }

    /// <summary>
    /// Verifies that very long messages are preserved exactly and InnerException is set.
    /// Inputs:
    ///  - message: a 5000-character string.
    ///  - innerException: a non-null exception.
    /// Expected:
    ///  - ex.Message equals the long message exactly.
    ///  - ex.InnerException equals the provided instance.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_VeryLongMessage_Preserved()
    {
        // Arrange
        var longMessage = new string('a', 5000);
        var inner = new Exception("inner");

        // Act
        var ex = new GithubApplicationInstallationException(longMessage, inner);

        // Assert
        ex.Message.Should().Be(longMessage);
        ex.InnerException.Should().BeSameAs(inner);
    }
}
