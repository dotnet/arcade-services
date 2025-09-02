// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Runtime.Serialization;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.Darc;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

public class DarcCoherencyExceptionTests
{
    private static IEnumerable<TestCaseData> Constructor_BuildsMessage_Cases()
    {
        yield return new TestCaseData(new string[] { })
            .SetName("Constructor_DependencyNamesEmpty_MessageEndsWithColonAnd Space");
        yield return new TestCaseData(new[] { "A" })
            .SetName("Constructor_SingleDependencyName_MessageContainsNameNoComma");
        yield return new TestCaseData(new[] { "Beta", "Alpha" })
            .SetName("Constructor_MultipleDependencyNames_MessageJoinedWithCommaAnd Space");
        yield return new TestCaseData(new[] { "", "   " })
            .SetName("Constructor_EmptyAndWhitespaceNames_MessageContainsSeparatorsAnd Spaces");
    }

    /// <summary>
    /// Verifies that the constructor composes the exception message from the provided dependency names
    /// and preserves the same Errors enumerable instance.
    /// Inputs:
    ///  - dependencyNames: A sequence of dependency names (including empty and whitespace cases).
    /// Expected:
    ///  - Message equals "Coherency update failed for the following dependencies: " followed by the names joined by ", ".
    ///  - Errors property references the exact same enumerable instance provided to the constructor.
    /// </summary>
    [TestCaseSource(nameof(Constructor_BuildsMessage_Cases))]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_DependencyNames_ComposesMessageAndStoresErrors(string[] dependencyNames)
    {
        // Arrange
        var errors = dependencyNames
            .Select(n => new CoherencyError { Dependency = new DependencyDetail { Name = n } })
            .ToList();

        // Act
        var exception = new DarcCoherencyException(errors);

        // Assert
        var expectedList = string.Join(", ", dependencyNames);
        var expectedMessage = "Coherency update failed for the following dependencies: " + expectedList;

        exception.Message.Should().Be(expectedMessage);
        exception.Errors.Should().BeSameAs(errors);
    }

    /// <summary>
    /// Ensures the single-parameter constructor creates the expected message and stores the provided error.
    /// Inputs:
    ///  - A CoherencyError with Dependency.Name parameterized across null, empty, whitespace, normal, special characters, and very long string.
    /// Expected:
    ///  - Exception.Message equals the fixed prefix plus the provided name (null treated as empty).
    ///  - Exception.Errors contains exactly one item which is the same instance as the provided CoherencyError.
    /// </summary>
    [TestCaseSource(nameof(DependencyNameCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DarcCoherencyException_SingleCoherencyError_BuildsMessageAndStoresError(string dependencyName)
    {
        // Arrange
        var coherencyError = new CoherencyError
        {
            Dependency = new DependencyDetail { Name = dependencyName },
            Error = "Some error",
            PotentialSolutions = new List<string> { "Try X", "Try Y" }
        };
        var expectedMessagePrefix = "Coherency update failed for the following dependencies: ";
        var expectedMessage = expectedMessagePrefix + (dependencyName ?? string.Empty);

        // Act
        var exception = new DarcCoherencyException(coherencyError);

        // Assert
        exception.Message.Should().Be(expectedMessage);
        exception.Errors.Should().NotBeNull();
        exception.Errors.Should().HaveCount(1);
        exception.Errors.Single().Should().BeSameAs(coherencyError);
    }

    /// <summary>
    /// Validates that passing a null CoherencyError to the single-parameter constructor throws a NullReferenceException.
    /// Inputs:
    ///  - coherencyError: null.
    /// Expected:
    ///  - A NullReferenceException is thrown during construction due to dereferencing a null error in the delegated constructor.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DarcCoherencyException_NullCoherencyError_ThrowsNullReferenceException()
    {
        // Arrange
        CoherencyError coherencyError = null;

        // Act
        Action act = () => new DarcCoherencyException(coherencyError);

        // Assert
        act.Should().Throw<NullReferenceException>();
    }

    /// <summary>
    /// Validates that providing a CoherencyError with a null Dependency to the single-parameter constructor throws a NullReferenceException.
    /// Inputs:
    ///  - coherencyError.Dependency: null.
    /// Expected:
    ///  - A NullReferenceException is thrown during construction when accessing Dependency.Name in the delegated constructor.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DarcCoherencyException_CoherencyErrorWithNullDependency_ThrowsNullReferenceException()
    {
        // Arrange
        var coherencyError = new CoherencyError
        {
            Dependency = null,
            Error = "Missing dependency",
            PotentialSolutions = new List<string>()
        };

        // Act
        Action act = () => new DarcCoherencyException(coherencyError);

        // Assert
        act.Should().Throw<NullReferenceException>();
    }

    private static IEnumerable<TestCaseData> DependencyNameCases()
    {
        yield return new TestCaseData(null).SetName("Name_Null");
        yield return new TestCaseData(string.Empty).SetName("Name_Empty");
        yield return new TestCaseData(" ").SetName("Name_Whitespace");
        yield return new TestCaseData("Package.A").SetName("Name_Normal");
        yield return new TestCaseData("Pkg,Name;!@#").SetName("Name_SpecialCharacters");
        yield return new TestCaseData(new string('x', 10000)).SetName("Name_VeryLong");
    }

    private class DarcCoherencyExceptionExposed : DarcCoherencyException
    {
        public DarcCoherencyExceptionExposed(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Verifies that the protected serialization constructor restores base Exception state while leaving
    /// the DarcCoherencyException-specific Errors property unset (null).
    /// Inputs:
    ///  - A DarcCoherencyException instance serialized into SerializationInfo using ISerializable.GetObjectData.
    /// Expected:
    ///  - Constructing via the protected (SerializationInfo, StreamingContext) constructor produces an instance
    ///    whose Message equals the original's Message and whose Errors is null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void SerializationConstructor_ValidSerializationInfo_MessageRestoredAndErrorsNull()
    {
        // Arrange
        var dependency = new DependencyDetail { Name = "Pkg.A", Version = "1.0.0", Commit = "sha-1", RepoUri = "https://repo/a" };
        var coherencyError = new CoherencyError
        {
            Dependency = dependency,
            Error = "Mismatch",
            PotentialSolutions = new List<string> { "Update dependency", "Align versions" }
        };
        var original = new DarcCoherencyException(new List<CoherencyError> { coherencyError });

        var info = new SerializationInfo(typeof(DarcCoherencyException), new FormatterConverter());
        var context = new StreamingContext(StreamingContextStates.All);
        ((ISerializable)original).GetObjectData(info, context);

        // Act
        var reconstructed = new DarcCoherencyExceptionExposed(info, context);

        // Assert
        reconstructed.Should().NotBeNull();
        reconstructed.Message.Should().Be(original.Message);
        reconstructed.Errors.Should().BeNull();
    }

    /// <summary>
    /// Ensures that constructing via the protected serialization constructor throws when required
    /// serialization fields are missing.
    /// Inputs:
    ///  - An empty SerializationInfo for DarcCoherencyException and a StreamingContext.
    /// Expected:
    ///  - Throw SerializationException due to missing required Exception fields.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void SerializationConstructor_MissingRequiredFields_ThrowsSerializationException()
    {
        // Arrange
        var info = new SerializationInfo(typeof(DarcCoherencyException), new FormatterConverter());
        var context = new StreamingContext(StreamingContextStates.All);

        // Act
        Action act = () => new DarcCoherencyExceptionExposed(info, context);

        // Assert
        act.Should().Throw<SerializationException>();
    }
}
