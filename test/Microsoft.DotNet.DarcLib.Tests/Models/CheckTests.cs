// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Models.UnitTests;

public class CheckTests
{
    /// <summary>
    /// Ensures that the constructor assigns the Status, Name, and Url properties as provided,
    /// and that IsMaestroMergePolicy defaults to false when omitted.
    /// Inputs:
    ///  - status: All defined CheckState values and an out-of-range value.
    ///  - name: "name"
    ///  - url: "url"
    /// Expected:
    ///  - Status equals the provided status (including out-of-range).
    ///  - Name equals "name".
    ///  - Url equals "url".
    ///  - IsMaestroMergePolicy is false by default.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(StatusCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_VariousStatusValues_AssignsProperties(CheckState status)
    {
        // Arrange
        var name = "name";
        var url = "url";

        // Act
        var check = new Check(status, name, url);

        // Assert
        check.Status.Should().Be(status);
        check.Name.Should().Be(name);
        check.Url.Should().Be(url);
        check.IsMaestroMergePolicy.Should().BeFalse();
    }

    /// <summary>
    /// Validates that the constructor correctly assigns string properties for a range of edge-case inputs.
    /// Inputs:
    ///  - name and url pairs including empty, whitespace-only, long strings, and special characters.
    ///  - status: Success (representative valid value).
    /// Expected:
    ///  - Name and Url properties exactly match the provided inputs without modification.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(NameAndUrlCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_StringEdgeCases_AssignedVerbatim(string name, string url)
    {
        // Arrange
        var status = CheckState.Success;

        // Act
        var check = new Check(status, name, url);

        // Assert
        check.Name.Should().Be(name);
        check.Url.Should().Be(url);
        check.Status.Should().Be(status);
        check.IsMaestroMergePolicy.Should().BeFalse();
    }

    /// <summary>
    /// Confirms that the IsMaestroMergePolicy flag is stored exactly as specified when explicitly provided.
    /// Inputs:
    ///  - status: Pending (representative valid value).
    ///  - name: "n"
    ///  - url: "u"
    ///  - isMaestroMergePolicy: true/false
    /// Expected:
    ///  - IsMaestroMergePolicy equals the provided boolean value.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCase(true)]
    [TestCase(false)]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_IsMaestroMergePolicySpecified_AssignsFlag(bool isMaestroMergePolicy)
    {
        // Arrange
        var status = CheckState.Pending;
        var name = "n";
        var url = "u";

        // Act
        var check = new Check(status, name, url, isMaestroMergePolicy);

        // Assert
        check.IsMaestroMergePolicy.Should().Be(isMaestroMergePolicy);
        check.Status.Should().Be(status);
        check.Name.Should().Be(name);
        check.Url.Should().Be(url);
    }

    private static IEnumerable<CheckState> StatusCases()
    {
        yield return CheckState.None;
        yield return CheckState.Pending;
        yield return CheckState.Error;
        yield return CheckState.Failure;
        yield return CheckState.Success;
        yield return (CheckState)(-1); // Out-of-range enum value
    }

    private static IEnumerable<TestCaseData> NameAndUrlCases()
    {
        yield return new TestCaseData(string.Empty, string.Empty);
        yield return new TestCaseData(" ", "     ");
        yield return new TestCaseData("simple-name", "http://example.com");
        yield return new TestCaseData("special-ç漢字\t\n", "https://exämple.com/p?q=✓&x=%20#line");
        yield return new TestCaseData(new string('a', 1024), new string('b', 2048));
        yield return new TestCaseData("path\\with\\backslashes", "file://C:/path with spaces/file.txt");
    }
}
