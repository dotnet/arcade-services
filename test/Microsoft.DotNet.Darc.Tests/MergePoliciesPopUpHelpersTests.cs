// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Maestro.MergePolicyEvaluation;
using Microsoft.DotNet.Darc.Models.PopUps;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Darc.Tests;

[TestFixture]
public class MergePoliciesPopUpHelpersTests
{
    [Test]
    public void ValidateMergePolicies_AllowsVersionDetailsPropsPolicyAlone()
    {
        // Arrange
        var logger = Mock.Of<ILogger>();
        var mergePolicies = new List<MergePolicy>
        {
            new() { Name = MergePolicyConstants.VersionDetailsPropsMergePolicyName, Properties = new Dictionary<string, Newtonsoft.Json.Linq.JToken>() }
        };

        // Act
        var result = MergePoliciesPopUpHelpers.ValidateMergePolicies(mergePolicies, logger);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void ValidateMergePolicies_AllowsStandardPolicyAlone()
    {
        // Arrange
        var logger = Mock.Of<ILogger>();
        var mergePolicies = new List<MergePolicy>
        {
            new() { Name = MergePolicyConstants.StandardMergePolicyName, Properties = new Dictionary<string, Newtonsoft.Json.Linq.JToken>() }
        };

        // Act
        var result = MergePoliciesPopUpHelpers.ValidateMergePolicies(mergePolicies, logger);

        // Assert
        result.Should().BeTrue();
    }

    [Test]
    public void ValidateMergePolicies_RejectsStandardAndVersionDetailsPropsTogether()
    {
        // Arrange
        var logger = new Mock<ILogger>();
        var mergePolicies = new List<MergePolicy>
        {
            new() { Name = MergePolicyConstants.StandardMergePolicyName, Properties = new Dictionary<string, Newtonsoft.Json.Linq.JToken>() },
            new() { Name = MergePolicyConstants.VersionDetailsPropsMergePolicyName, Properties = new Dictionary<string, Newtonsoft.Json.Linq.JToken>() }
        };

        // Act
        var result = MergePoliciesPopUpHelpers.ValidateMergePolicies(mergePolicies, logger.Object);

        // Assert
        result.Should().BeFalse();
        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Cannot combine Standard and VersionDetailsPropsMergePolicy")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), Times.Once);
    }
}