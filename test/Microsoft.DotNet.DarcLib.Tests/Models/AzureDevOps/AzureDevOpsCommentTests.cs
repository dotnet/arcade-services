// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

public class AzureDevOpsCommentTests
{
    /// <summary>
    /// Ensures the constructor assigns the provided list reference directly to the Comments property without cloning.
    /// Inputs:
    ///  - A valid List&lt;AzureDevOpsCommentBody&gt; instance (empty, single-item, multi-item, or with duplicates).
    /// Expected:
    ///  - Comments references the exact same list instance.
    ///  - The count and element references are preserved.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(ValidCommentLists))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AzureDevOpsComment_ValidLists_AssignsSameReferenceAndPreservesItems(List<AzureDevOpsCommentBody> input)
    {
        // Arrange
        var original = input;

        // Act
        var sut = new AzureDevOpsComment(original);

        // Assert
        sut.Comments.Should().BeSameAs(original);
        sut.Comments.Should().HaveCount(original.Count);
        for (int i = 0; i < original.Count; i++)
        {
            sut.Comments[i].Should().BeSameAs(original[i]);
        }
    }

    /// <summary>
    /// Validates the constructor behavior when the provided comments list is null.
    /// Inputs:
    ///  - comments: null.
    /// Expected:
    ///  - Comments is set to null (no exception is thrown).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void AzureDevOpsComment_NullList_AllowsNullAssignment()
    {
        // Arrange
        List<AzureDevOpsCommentBody> input = null;

        // Act
        var sut = new AzureDevOpsComment(input);

        // Assert
        sut.Comments.Should().BeNull();
    }

    private static IEnumerable ValidCommentLists()
    {
        yield return new TestCaseData(new List<AzureDevOpsCommentBody>())
            .SetName("AzureDevOpsComment_EmptyList_AssignsSameReference");

        var single = new List<AzureDevOpsCommentBody>
            {
                new AzureDevOpsCommentBody("single")
            };
        yield return new TestCaseData(single)
            .SetName("AzureDevOpsComment_SingleItem_AssignsSameReference");

        var multiple = new List<AzureDevOpsCommentBody>
            {
                new AzureDevOpsCommentBody("first"),
                new AzureDevOpsCommentBody("second"),
                new AzureDevOpsCommentBody("third")
            };
        yield return new TestCaseData(multiple)
            .SetName("AzureDevOpsComment_MultipleItems_AssignsSameReference");

        var duplicateRef = new AzureDevOpsCommentBody("dup");
        var duplicates = new List<AzureDevOpsCommentBody>
            {
                duplicateRef,
                duplicateRef
            };
        yield return new TestCaseData(duplicates)
            .SetName("AzureDevOpsComment_DuplicateReferences_PreservesDuplicates");
    }
}
