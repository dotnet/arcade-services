// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

public class CredScanSuppressionsGeneratorTests
{
    /// <summary>
    /// Verifies that the constructor accepts valid Strict/Loose mocks for all dependencies and
    /// instantiates a non-null object implementing ICredScanSuppressionsGenerator without invoking any dependency members.
    /// Inputs:
    ///  - useStrict: when true, all mocks are created with MockBehavior.Strict; otherwise MockBehavior.Loose.
    /// Expected:
    ///  - No exceptions thrown.
    ///  - Instance is not null and is assignable to ICredScanSuppressionsGenerator.
    ///  - No interactions occur with the provided dependencies during construction.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_WithStrictOrLooseMocks_InstanceCreatedAndNoDependencyCalls(bool useStrict)
    {
        // Arrange
        var behavior = useStrict ? MockBehavior.Strict : MockBehavior.Loose;

        var vmrInfoMock = new Mock<IVmrInfo>(behavior);
        var sourceManifestMock = new Mock<ISourceManifest>(behavior);
        var localGitClientMock = new Mock<ILocalGitClient>(behavior);
        var fileSystemMock = new Mock<IFileSystem>(behavior);
        var loggerMock = new Mock<ILogger<CredScanSuppressionsGenerator>>(behavior);

        // Act
        var sut = new CredScanSuppressionsGenerator(
            vmrInfoMock.Object,
            sourceManifestMock.Object,
            localGitClientMock.Object,
            fileSystemMock.Object,
            loggerMock.Object);

        // Assert
        sut.Should().NotBeNull();
        sut.Should().BeAssignableTo<ICredScanSuppressionsGenerator>();

        vmrInfoMock.VerifyNoOtherCalls();
        sourceManifestMock.VerifyNoOtherCalls();
        localGitClientMock.VerifyNoOtherCalls();
        fileSystemMock.VerifyNoOtherCalls();
        loggerMock.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Placeholder test documenting that null-argument validation cannot be exercised because the constructor
    /// parameters are non-nullable and the implementation does not perform explicit null checks.
    /// Inputs:
    ///  - N/A
    /// Expected:
    ///  - Marked inconclusive with guidance for future adjustments if nullability rules change.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void Constructor_NullArguments_NotTestedDueToNonNullableAnnotations()
    {
        // Arrange / Act / Assert
        // The source enables nullable reference types and the constructor parameters are non-nullable.
        // Repository test guidelines prohibit assigning null to non-nullable types in tests.
        // If the implementation later introduces explicit null checks or parameters become nullable,
        // add targeted tests to validate ArgumentNullException behavior.
        Assert.Inconclusive("Null-argument scenarios are not testable under current non-nullable parameter annotations and repository guidelines.");
    }
}

/// <summary>
/// Tests for SingleStringOrArrayConverter.Write ensuring it serializes the provided List&lt;string&gt;
/// using JsonSerializer with the runtime type and provided options.
/// Inputs:
///  - Various List&lt;string&gt; instances (empty, single item with special characters, multi-item, long string).
///  - Both indented and non-indented JsonSerializerOptions.
/// Expected:
///  - The JSON written by the converter matches JsonSerializer.Serialize(list, list.GetType(), options).
/// </summary>
public class SingleStringOrArrayConverterTests
{
    /// <summary>
    /// Provides diverse input lists and indentation settings to validate converter output against JsonSerializer.
    /// </summary>
    public static IEnumerable WriteCases()
    {
        // Empty list, not indented
        yield return new TestCaseData(new List<string>(), false).SetName("EmptyList_NotIndented");

        // Single item with special characters that require escaping, indented
        yield return new TestCaseData(
            new List<string> { "a\"b\\c\n\t\r\u2603" },
            true).SetName("SingleItemWithEscapes_Indented");

        // Multiple items, not indented
        yield return new TestCaseData(
            new List<string> { "alpha", "beta", "gamma" },
            false).SetName("MultipleItems_NotIndented");

        // Very long string item, indented
        yield return new TestCaseData(
            new List<string> { new string('x', 8192) },
            true).SetName("VeryLongString_Indented");

        // Derived list type to ensure runtime type is respected, not indented
        var derived = new CustomStringList { "one", "two" };
        yield return new TestCaseData(derived, false).SetName("DerivedListRuntimeType_NotIndented");
    }

    /// <summary>
    /// Validates that Write serializes the list using JsonSerializer with the runtime type and given options,
    /// producing output identical to JsonSerializer.Serialize(list, list.GetType(), options).
    /// </summary>
    /// <param name="list">Input list to be serialized.</param>
    /// <param name="writeIndented">Whether JSON should be indented.</param>
    [Test]
    [TestCaseSource(nameof(WriteCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Write_VariousLists_MatchesJsonSerializerOutput(List<string> list, bool writeIndented)
    {
        // Arrange
        var converter = new SingleStringOrArrayConverter();
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented
        };

        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = writeIndented });

        // Act
        converter.Write(writer, list, options);
        writer.Flush();

        var actualJson = Encoding.UTF8.GetString(ms.ToArray());
        var expectedJson = JsonSerializer.Serialize(list, list.GetType(), options);

        // Assert
        actualJson.Should().Be(expectedJson);
    }

    private class CustomStringList : List<string>
    {
    }
}
