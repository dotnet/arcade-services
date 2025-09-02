// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Microsoft.DotNet;
using Microsoft.DotNet.DarcLib;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.UnitTests;


public class IGitRepoExtensionTests
{
    /// <summary>
    /// Verifies that valid Base64 content is decoded and returned as a string.
    /// Inputs:
    ///  - A mocked IRemoteGitRepo instance (not used by the extension).
    ///  - Base64-encoded content for various input strings (including empty and Unicode).
    /// Expected:
    ///  - The method returns the decoded string matching the original input.
    /// </summary>
    [TestCase("")]
    [TestCase("Hello, world!")]
    [TestCase("   ")]
    [TestCase("Text with unicode — π 😀")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDecodedContent_ValidBase64_ReturnsDecodedString(string original)
    {
        // Arrange
        var gitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Loose).Object;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(original));

        // Act
        var result = IGitRepoExtension.GetDecodedContent(gitRepo, encoded);

        // Assert
        result.Should().Be(original);
    }

    /// <summary>
    /// Ensures that when the input is not valid Base64, the original input is returned unchanged.
    /// Inputs:
    ///  - A mocked IRemoteGitRepo instance (not used by the extension).
    ///  - Various invalid Base64 strings.
    /// Expected:
    ///  - The method catches FormatException and returns the original string.
    /// </summary>
    [TestCase("not-base64")]
    [TestCase("%%%###")]
    [TestCase("123?abc")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDecodedContent_InvalidBase64_ReturnsOriginalString(string invalid)
    {
        // Arrange
        var gitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Loose).Object;

        // Act
        var result = IGitRepoExtension.GetDecodedContent(gitRepo, invalid);

        // Assert
        result.Should().Be(invalid);
    }

    /// <summary>
    /// Validates that when the Base64 content includes a UTF-8 BOM, it is not included in the returned string.
    /// Inputs:
    ///  - A mocked IRemoteGitRepo instance (not used by the extension).
    ///  - Base64-encoded bytes that start with the UTF-8 BOM followed by ASCII text.
    /// Expected:
    ///  - The method returns the decoded string without a leading BOM character.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetDecodedContent_ValidBase64WithUtf8Bom_BomIsNotReturned()
    {
        // Arrange
        var gitRepo = new Mock<IRemoteGitRepo>(MockBehavior.Loose).Object;
        const string text = "Hello";
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var payload = new byte[bom.Length + Encoding.UTF8.GetByteCount(text)];
        Buffer.BlockCopy(bom, 0, payload, 0, bom.Length);
        Buffer.BlockCopy(Encoding.UTF8.GetBytes(text), 0, payload, bom.Length, Encoding.UTF8.GetByteCount(text));
        var encoded = Convert.ToBase64String(payload);

        // Act
        var result = IGitRepoExtension.GetDecodedContent(gitRepo, encoded);

        // Assert
        result.Should().Be(text);
    }
}
