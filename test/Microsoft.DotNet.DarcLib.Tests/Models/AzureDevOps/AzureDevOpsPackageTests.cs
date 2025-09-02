// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

public class AzureDevOpsPackageTests
{
    /// <summary>
    /// Validates that the AzureDevOpsPackage constructor assigns provided name and protocolType directly
    /// and leaves Versions untouched (null).
    /// Inputs:
    ///  - name and protocolType values covering null, empty, whitespace-only, very long strings, and special characters.
    /// Expected:
    ///  - The constructed instance has Name and ProtocolType equal to the inputs.
    ///  - Versions remains null.
    ///  - No exception is thrown.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(Constructor_AssignsInputsAndLeavesVersionsNull_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AzureDevOpsPackage_Constructor_AssignsInputsAndLeavesVersionsNull(string name, string protocolType)
    {
        // Arrange
        // (Inputs supplied via TestCaseSource)

        // Act
        var pkg = new AzureDevOpsPackage(name, protocolType);

        // Assert
        pkg.Name.Should().Be(name);
        pkg.ProtocolType.Should().Be(protocolType);
        pkg.Versions.Should().BeNull();
    }

    private static IEnumerable<TestCaseData> Constructor_AssignsInputsAndLeavesVersionsNull_Cases()
    {
        yield return new TestCaseData(null, null).SetName("Ctor_NullNameAndNullProtocol_AssignsAndLeavesVersionsNull");
        yield return new TestCaseData(string.Empty, string.Empty).SetName("Ctor_EmptyStrings_AssignsAndLeavesVersionsNull");
        yield return new TestCaseData(" ", "   ").SetName("Ctor_WhitespaceOnly_AssignsAndLeavesVersionsNull");
        yield return new TestCaseData(new string('n', 1024), "nuget").SetName("Ctor_VeryLongName_AssignsAndLeavesVersionsNull");
        yield return new TestCaseData("Package.X", new string('p', 2048)).SetName("Ctor_VeryLongProtocol_AssignsAndLeavesVersionsNull");
        yield return new TestCaseData("Pack\nage\tX😊", "nuget+v3").SetName("Ctor_SpecialCharacters_AssignsAndLeavesVersionsNull");
        yield return new TestCaseData("Package.X", "nuget").SetName("Ctor_TypicalValues_AssignsAndLeavesVersionsNull");
    }
}
