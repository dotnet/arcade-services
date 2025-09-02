using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Moq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;


namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo.UnitTests;

/// <summary>
/// Unit tests for ConflictInPrBranchException public constructor.
/// Focuses on parsing of failure messages via ParseResult and correct initialization
/// of ConflictedFiles and the exception Message.
/// </summary>
public class ConflictInPrBranchExceptionTests
{
    /// <summary>
    /// Verifies that for forward flow, known error formats are parsed,
    /// "src/{mapping}/{path}" is normalized to "{path}",
    /// and the exception message targets the provided branch.
    /// Inputs:
    ///  - failedMergeMessage containing one recognized error line.
    ///  - targetBranch name.
    ///  - mappingName used within the VMR path (only relevant to normalization).
    ///  - isForwardFlow = true.
    /// Expected:
    ///  - ConflictedFiles contains the normalized path (without "src/{mapping}/" prefix).
    ///  - Exception.Message is "Failed to flow changes due to conflicts in the target branch ({targetBranch})".
    /// </summary>
    [TestCase("patch failed: src/repo/path/file1.cs: already exist in index", "path/file1.cs")]
    [TestCase("error: patch failed: src/repo/path2/file2.cs:", "path2/file2.cs")]
    [TestCase("error: src/repo/a/b/file3.cs: patch does not apply", "a/b/file3.cs")]
    [TestCase("error: src/repo/c/d/file4.cs: does not exist in index", "c/d/file4.cs")]
    [TestCase("CONFLICT (content): Merge conflict in src/repo/e/f/file5.cs", "e/f/file5.cs")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_ForwardFlow_KnownPatternsAreNormalized(string failedMergeMessage, string expectedNormalizedPath)
    {
        // Arrange
        var targetBranch = "feature/my-branch";
        var mappingName = "repo";
        var isForwardFlow = true;

        // Act
        var ex = new ConflictInPrBranchException(failedMergeMessage, targetBranch, mappingName, isForwardFlow);

        // Assert
        ex.Message.Should().Be($"Failed to flow changes due to conflicts in the target branch ({targetBranch})");
        ex.ConflictedFiles.Should().BeEquivalentTo(new[] { expectedNormalizedPath });
    }

    /// <summary>
    /// Ensures that for backflow, parsed file paths are de-duplicated and then
    /// prefixed with "src/{mappingName}/". Also verifies that unrelated lines are ignored.
    /// Inputs:
    ///  - failedMergeMessage with duplicate file references using different error formats and an unrelated line.
    ///  - isForwardFlow = false.
    ///  - mappingName = "mymap".
    /// Expected:
    ///  - ConflictedFiles contains a single prefixed path "src/mymap/file.cs", without duplicates.
    ///  - Exception message targets the specified branch.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_Backflow_DeduplicatesAndPrefixesFiles()
    {
        // Arrange
        var lines = new[]
        {
                "error: patch failed: file.cs:",
                "error: file.cs: does not exist in index",
                "some unrelated line that should be ignored"
            };
        var failedMergeMessage = string.Join(Environment.NewLine, lines);
        var targetBranch = "release/9.0";
        var mappingName = "mymap";
        var isForwardFlow = false;

        // Act
        var ex = new ConflictInPrBranchException(failedMergeMessage, targetBranch, mappingName, isForwardFlow);

        // Assert
        ex.Message.Should().Be($"Failed to flow changes due to conflicts in the target branch ({targetBranch})");
        ex.ConflictedFiles.Should().BeEquivalentTo(new[] { "src/mymap/file.cs" });
    }

    /// <summary>
    /// Validates that an empty failure message yields no conflicted files while
    /// still setting the correct exception message.
    /// Inputs:
    ///  - failedMergeMessage = "".
    ///  - isForwardFlow can be either; forward is chosen here.
    /// Expected:
    ///  - ConflictedFiles is empty.
    ///  - Exception message includes the target branch.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_EmptyFailureMessage_ConflictedFilesEmpty()
    {
        // Arrange
        var failedMergeMessage = string.Empty;
        var targetBranch = "main";
        var mappingName = "repo";
        var isForwardFlow = true;

        // Act
        var ex = new ConflictInPrBranchException(failedMergeMessage, targetBranch, mappingName, isForwardFlow);

        // Assert
        ex.Message.Should().Be($"Failed to flow changes due to conflicts in the target branch ({targetBranch})");
        ex.ConflictedFiles.Should().BeEquivalentTo(Array.Empty<string>());
    }
}

