// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.GitHub;
using Moq;
using NUnit.Framework;
using System;


namespace Microsoft.DotNet.DarcLib.Models.GitHub.UnitTests;

public class GitHubRefTests
{
    /// <summary>
    /// Ensures the constructor assigns Ref and Sha exactly as provided and leaves Force at its default (false).
    /// Inputs (via TestCaseSource):
    ///  - Typical ref/sha, empty strings, whitespace-only, very long strings, special/control characters, and embedded null characters.
    /// Expected:
    ///  - Ref equals the provided ref.
    ///  - Sha equals the provided sha.
    ///  - Force equals false.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(ConstructorCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AssignsInputsToProperties_PropertiesMatchAndForceDefaultsToFalse(string githubRef, string sha)
    {
        // Arrange
        // (inputs provided by TestCaseSource)

        // Act
        var sut = new GitHubRef(githubRef, sha);

        // Assert
        sut.Ref.Should().Be(githubRef);
        sut.Sha.Should().Be(sha);
        sut.Force.Should().Be(false);
    }

    private static System.Collections.Generic.IEnumerable<TestCaseData> ConstructorCases()
    {
        yield return new TestCaseData("refs/heads/main", new string('a', 40))
            .SetName("TypicalRefAndSha");
        yield return new TestCaseData(string.Empty, string.Empty)
            .SetName("EmptyStrings");
        yield return new TestCaseData("   ", "   ")
            .SetName("WhitespaceOnly");
        yield return new TestCaseData(new string('x', 10000), new string('y', 10000))
            .SetName("VeryLongStrings");
        yield return new TestCaseData("refs/tags/v1.0.0-β✓\n\t\r", "deadbeefcafebabe\n\t\r☃")
            .SetName("SpecialAndControlCharacters");
        yield return new TestCaseData("abc\0def", "123\0456")
            .SetName("EmbeddedNullCharacters");
    }
}
