// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Moq;
using NUnit.Framework;


namespace Microsoft.DotNet.DarcLib.Helpers.UnitTests;

public class GitFileContentContainerTests
{
    /// <summary>
    /// Ensures that when none of the properties are set, the method still returns a list of three entries
    /// corresponding to VersionDetailsXml, GlobalJson, and NugetConfig, all of which are null.
    /// Inputs:
    ///  - A newly constructed GitFileContentContainer with no properties assigned.
    /// Expected:
    ///  - A list with exactly 3 items, each being null.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetFilesToCommit_AllBaseFilesUnset_ReturnsListWithThreeNullEntries()
    {
        // Arrange
        var container = new GitFileContentContainer();

        // Act
        var result = container.GetFilesToCommit();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result[0].Should().BeNull();
        result[1].Should().BeNull();
        result[2].Should().BeNull();
    }

    /// <summary>
    /// Verifies that optional files are conditionally appended in the correct order after the three base entries.
    /// Inputs:
    ///  - Three base files always set (VersionDetailsXml, GlobalJson, NugetConfig).
    ///  - Variations of optional files (DotNetToolsJson, VersionDetailsProps, VersionProps) being set or left null.
    /// Expected:
    ///  - Returned list begins with the three base files in order, followed by any non-null optional files
    ///    in the precise order: DotNetToolsJson, VersionDetailsProps, VersionProps.
    /// </summary>
    [TestCase(false, false, false, 3, TestName = "GetFilesToCommit_OptionalFilesPresence_None_OnlyBaseItems")]
    [TestCase(true, false, true, 5, TestName = "GetFilesToCommit_OptionalFilesPresence_DotNetToolsAndVersionProps_AppendedInOrder")]
    [TestCase(true, true, true, 6, TestName = "GetFilesToCommit_OptionalFilesPresence_All_AppendedInOrder")]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetFilesToCommit_OptionalFilesPresence_ProducesExpectedOrderAndCount(
        bool includeDotNetToolsJson,
        bool includeVersionDetailsProps,
        bool includeVersionProps,
        int expectedCount)
    {
        // Arrange
        var baseVersionDetailsXml = new GitFile("version.details.xml", "<root/>");
        var baseGlobalJson = new GitFile("global.json", "{ }");
        var baseNugetConfig = new GitFile("NuGet.Config", "<configuration/>");

        var container = new GitFileContentContainer
        {
            VersionDetailsXml = baseVersionDetailsXml,
            GlobalJson = baseGlobalJson,
            NugetConfig = baseNugetConfig
        };

        GitFile dotnetToolsJson = new GitFile(".config/dotnet-tools.json", "{ \"tools\": {} }");
        GitFile versionDetailsProps = new GitFile("eng/Version.Details.props", "<Project/>");
        GitFile versionProps = new GitFile("eng/Versions.props", "<Project/>");

        if (includeDotNetToolsJson)
        {
            container.DotNetToolsJson = dotnetToolsJson;
        }

        if (includeVersionDetailsProps)
        {
            container.VersionDetailsProps = versionDetailsProps;
        }

        if (includeVersionProps)
        {
            container.VersionProps = versionProps;
        }

        // Act
        var result = container.GetFilesToCommit();

        // Assert
        result.Should().HaveCount(expectedCount);
        result[0].Should().BeSameAs(baseVersionDetailsXml);
        result[1].Should().BeSameAs(baseGlobalJson);
        result[2].Should().BeSameAs(baseNugetConfig);

        var index = 3;

        if (includeDotNetToolsJson)
        {
            result[index].Should().BeSameAs(dotnetToolsJson);
            index++;
        }

        if (includeVersionDetailsProps)
        {
            result[index].Should().BeSameAs(versionDetailsProps);
            index++;
        }

        if (includeVersionProps)
        {
            result[index].Should().BeSameAs(versionProps);
            index++;
        }
    }

    /// <summary>
    /// Confirms that duplicate GitFile instances assigned to different properties are preserved without deduplication
    /// and the final list maintains the expected ordering.
    /// Inputs:
    ///  - Same GitFile instance reused across multiple properties (base and optional).
    /// Expected:
    ///  - The returned list contains duplicates in the exact order they were added.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void GetFilesToCommit_DuplicateGitFileInstances_PreservedAndOrdered()
    {
        // Arrange
        var gfA = new GitFile("A.txt", "A");
        var gfB = new GitFile("B.txt", "B");

        var container = new GitFileContentContainer
        {
            VersionDetailsXml = gfA,
            GlobalJson = gfB,
            NugetConfig = gfA,
            DotNetToolsJson = gfA,
            VersionDetailsProps = gfB,
            VersionProps = gfA
        };

        // Act
        var result = container.GetFilesToCommit();

        // Assert
        result.Should().HaveCount(6);
        result[0].Should().BeSameAs(gfA);
        result[1].Should().BeSameAs(gfB);
        result[2].Should().BeSameAs(gfA);
        result[3].Should().BeSameAs(gfA);
        result[4].Should().BeSameAs(gfB);
        result[5].Should().BeSameAs(gfA);
    }
}

