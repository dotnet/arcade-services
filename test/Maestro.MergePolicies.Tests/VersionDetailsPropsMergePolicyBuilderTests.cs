// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using Maestro.MergePolicyEvaluation;
using Microsoft.Extensions.DependencyInjection;

namespace Maestro.MergePolicies.Tests;

[TestFixture]
public class VersionDetailsPropsMergePolicyBuilderTests
{
    [Test]
    public void VersionDetailsPropsMergePolicyBuilder_IsRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMergePolicies();
        var serviceProvider = services.BuildServiceProvider();
        
        // Act
        var builders = serviceProvider.GetServices<IMergePolicyBuilder>();
        var versionDetailsPropsPolicyBuilder = builders.FirstOrDefault(b => b.Name == MergePolicyConstants.VersionDetailsPropsMergePolicyName);
        
        // Assert
        versionDetailsPropsPolicyBuilder.Should().NotBeNull();
        versionDetailsPropsPolicyBuilder.Should().BeOfType<VersionDetailsPropsMergePolicyBuilder>();
    }

    [Test]
    public async Task VersionDetailsPropsMergePolicyBuilder_BuildsPolicyCorrectly()
    {
        // Arrange
        var builder = new VersionDetailsPropsMergePolicyBuilder();
        var properties = new MergePolicyProperties(new Dictionary<string, Newtonsoft.Json.Linq.JToken>());
        var pr = new PullRequestUpdateSummary(
            url: "https://github.com/dotnet/core/pull/12345",
            coherencyCheckSuccessful: true,
            coherencyErrors: new List<CoherencyErrorDetails>(),
            requiredUpdates: new List<DependencyUpdateSummary>(),
            containedUpdates: new List<SubscriptionUpdateSummary>(),
            headBranch: "test-branch",
            repoUrl: "https://github.com/dotnet/core",
            codeFlowDirection: CodeFlowDirection.None);

        // Act
        var policies = await builder.BuildMergePoliciesAsync(properties, pr);

        // Assert
        policies.Should().HaveCount(1);
        policies.First().Should().BeOfType<VersionDetailsPropsMergePolicy>();
    }
}