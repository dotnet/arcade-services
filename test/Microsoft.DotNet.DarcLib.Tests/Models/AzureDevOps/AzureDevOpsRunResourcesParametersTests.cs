// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.AzureDevOps;
using Moq;
using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;


namespace Microsoft.DotNet.DarcLib.Models.AzureDevOps.UnitTests;

public class AzureDevOpsRunResourcesParametersTests
{
    /// <summary>
    /// Ensures the parameterless constructor initializes both Repositories and Pipelines
    /// to non-null, empty, and distinct Dictionary instances of the expected generic types.
    /// Inputs:
    ///  - No inputs (default constructor).
    /// Expected:
    ///  - Repositories and Pipelines are not null.
    ///  - Repositories and Pipelines are empty.
    ///  - Repositories and Pipelines are instances of Dictionary with expected generic arguments.
    ///  - Repositories and Pipelines reference different dictionary instances.
    /// </summary>
    [Test]
    [Category("auto-generated")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    public void AzureDevOpsRunResourcesParameters_Constructor_InitializesEmptyDictionaries()
    {
        // Arrange

        // Act
        var sut = new AzureDevOpsRunResourcesParameters();

        // Assert
        sut.Repositories.Should().NotBeNull();
        sut.Pipelines.Should().NotBeNull();

        sut.Repositories.Should().BeEmpty();
        sut.Pipelines.Should().BeEmpty();

        sut.Repositories.Should().BeOfType<Dictionary<string, AzureDevOpsRepositoryResourceParameter>>();
        sut.Pipelines.Should().BeOfType<Dictionary<string, AzureDevOpsPipelineResourceParameter>>();

        sut.Repositories.Should().NotBeSameAs(sut.Pipelines);
    }
}
