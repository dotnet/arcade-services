// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

public class AzureDevOpsPipelineResourceParameterTests
{
    private static readonly object[] VersionValues =
    {
            new object[] { null },
            new object[] { string.Empty },
            new object[] { " " },
            new object[] { " \t\r\n" },
            new object[] { "1.0.0" },
            new object[] { "v1.0-rc+build.1" },
            new object[] { "漢字🚀\t\n\0" },
            new object[] { new string('a', 10000) },
        };

    /// <summary>
    /// Verifies that the constructor assigns the provided version value to the Version property without modification.
    /// Inputs:
    ///  - Various string values including null, empty, whitespace-only, long, and special-character-containing strings.
    /// Expected:
    ///  - The Version property equals the provided input exactly (including null and whitespace).
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [TestCaseSource(nameof(VersionValues))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_AssignsVersion_Verbatim(string versionInput)
    {
        // Arrange
        // (No additional arrangement required; input parameter is provided by TestCaseSource)

        // Act
        var parameter = new AzureDevOpsPipelineResourceParameter(versionInput);

        // Assert
        parameter.Version.Should().Be(versionInput);
    }
}
