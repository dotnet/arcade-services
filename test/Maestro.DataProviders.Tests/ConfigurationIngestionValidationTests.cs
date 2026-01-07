// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.DataProviders.ConfigurationIngestion.Model;
using Maestro.DataProviders.ConfigurationIngestion.Validations;
using Microsoft.DotNet.MaestroConfiguration.Client.Models;

namespace Maestro.DataProviders.Tests;

[TestFixture]
public class ConfigurationIngestionValidationTests
{
    #region SubscriptionValidator Tests

    [Test]
    public void ValidateSubscription_NullChannel_ThrowsWithEntityInfo()
    {
        // Arrange
        var subscription = new IngestedSubscription(new SubscriptionYaml
        {
            Id = Guid.NewGuid(),
            Channel = null!,
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => SubscriptionValidator.ValidateSubscription(subscription));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Channel name is required");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain(subscription.Values.Id.ToString());
        exception.EntityInfo.Should().Contain(subscription.Values.Id.ToString());
    }

    [Test]
    public void ValidateSubscription_BatchableWithMergePolicies_ThrowsWithEntityInfo()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var subscription = new IngestedSubscription(new SubscriptionYaml
        {
            Id = subscriptionId,
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            Batchable = true,
            MergePolicies = [new MergePolicyYaml { Name = "AllChecksSuccessful" }],
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => SubscriptionValidator.ValidateSubscription(subscription));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Batchable subscriptions cannot be combined with merge policies");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain(subscriptionId.ToString());
        exception.EntityInfo.Should().Contain(subscriptionId.ToString());
    }

    [Test]
    public void ValidateSubscription_CodeflowWithoutSourceEnabled_ThrowsWithEntityInfo()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var subscription = new IngestedSubscription(new SubscriptionYaml
        {
            Id = subscriptionId,
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
            SourceEnabled = false,
            MergePolicies = [new MergePolicyYaml { Name = "CodeflowConsistency" }],
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => SubscriptionValidator.ValidateSubscription(subscription));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Only source-enabled subscriptions may have the Codeflow merge policy");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain(subscriptionId.ToString());
    }

    #endregion

    #region ChannelValidator Tests

    [Test]
    public void ValidateChannel_NullName_ThrowsWithEntityInfo()
    {
        // Arrange
        var channel = new IngestedChannel(new ChannelYaml
        {
            Name = null!,
            Classification = "release",
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => ChannelValidator.ValidateChannel(channel));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Channel name is required");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain("Channel");
    }

    [Test]
    public void ValidateChannel_NullClassification_ThrowsWithEntityInfo()
    {
        // Arrange
        var channel = new IngestedChannel(new ChannelYaml
        {
            Name = ".NET 8",
            Classification = null!,
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => ChannelValidator.ValidateChannel(channel));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Channel classification is required");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain(".NET 8");
        exception.EntityInfo.Should().Contain(".NET 8");
    }

    #endregion

    #region DefaultChannelValidator Tests

    [Test]
    public void ValidateDefaultChannel_NullRepository_ThrowsWithEntityInfo()
    {
        // Arrange
        var defaultChannel = new IngestedDefaultChannel(new DefaultChannelYaml
        {
            Repository = null!,
            Branch = "main",
            Channel = ".NET 8",
            Enabled = true,
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => DefaultChannelValidator.ValidateDefaultChannel(defaultChannel));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Default channel repository is required");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain("main");
        exception.Message.Should().Contain(".NET 8");
    }

    [Test]
    public void ValidateDefaultChannel_RepositoryTooLong_ThrowsWithEntityInfo()
    {
        // Arrange
        var longRepo = new string('a', 301);
        var defaultChannel = new IngestedDefaultChannel(new DefaultChannelYaml
        {
            Repository = longRepo,
            Branch = "main",
            Channel = ".NET 8",
            Enabled = true,
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => DefaultChannelValidator.ValidateDefaultChannel(defaultChannel));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Default channel repository cannot be longer than 300 characters");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain("main");
        exception.Message.Should().Contain(".NET 8");
    }

    [Test]
    public void ValidateDefaultChannel_BranchTooLong_ThrowsWithEntityInfo()
    {
        // Arrange
        var longBranch = new string('b', 101);
        var defaultChannel = new IngestedDefaultChannel(new DefaultChannelYaml
        {
            Repository = "https://github.com/dotnet/runtime",
            Branch = longBranch,
            Channel = ".NET 8",
            Enabled = true,
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => DefaultChannelValidator.ValidateDefaultChannel(defaultChannel));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Default channel branch name cannot be longer than 100 characters");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain("https://github.com/dotnet/runtime");
        exception.Message.Should().Contain(".NET 8");
    }

    #endregion

    #region BranchMergePolicyValidator Tests

    [Test]
    public void ValidateBranchMergePolicies_NullRepository_ThrowsWithEntityInfo()
    {
        // Arrange
        var branchMergePolicy = new IngestedBranchMergePolicies(new BranchMergePoliciesYaml
        {
            Repository = null!,
            Branch = "main",
            MergePolicies = [new MergePolicyYaml { Name = "AllChecksSuccessful" }],
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => BranchMergePolicyValidator.ValidateBranchMergePolicies(branchMergePolicy));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Repository is required");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain("main");
    }

    [Test]
    public void ValidateBranchMergePolicies_RepositoryTooLong_ThrowsWithEntityInfo()
    {
        // Arrange
        var longRepo = new string('a', 451);
        var branchMergePolicy = new IngestedBranchMergePolicies(new BranchMergePoliciesYaml
        {
            Repository = longRepo,
            Branch = "main",
            MergePolicies = [new MergePolicyYaml { Name = "AllChecksSuccessful" }],
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => BranchMergePolicyValidator.ValidateBranchMergePolicies(branchMergePolicy));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("Repository name cannot be longer than 450");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain("main");
    }

    [Test]
    public void ValidateBranchMergePolicies_StandardPolicyConflict_ThrowsWithEntityInfo()
    {
        // Arrange
        var branchMergePolicy = new IngestedBranchMergePolicies(new BranchMergePoliciesYaml
        {
            Repository = "https://github.com/dotnet/runtime",
            Branch = "main",
            MergePolicies =
            [
                new MergePolicyYaml { Name = "Standard" },
                new MergePolicyYaml { Name = "AllChecksSuccessful" },
            ],
        });

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => BranchMergePolicyValidator.ValidateBranchMergePolicies(branchMergePolicy));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("One or more of the following merge policies could not be added");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain("https://github.com/dotnet/runtime");
        exception.Message.Should().Contain("main");
    }

    #endregion

    #region EntityValidator Tests

    [Test]
    public void ValidateEntityUniqueness_DuplicateChannels_ThrowsWithEntityInfo()
    {
        // Arrange
        var channels = new[]
        {
            new IngestedChannel(new ChannelYaml { Name = ".NET 8", Classification = "release" }),
            new IngestedChannel(new ChannelYaml { Name = ".NET 8", Classification = "dev" }),
        };

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => EntityValidator.ValidateEntityUniqueness(channels));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("collection contains duplicate Ids");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain(".NET 8");
    }

    [Test]
    public void ValidateEntityUniqueness_DuplicateSubscriptions_ThrowsWithEntityInfo()
    {
        // Arrange
        var sharedId = Guid.NewGuid();
        var subscriptions = new[]
        {
            new IngestedSubscription(new SubscriptionYaml
            {
                Id = sharedId,
                Channel = ".NET 8",
                SourceRepository = "https://github.com/dotnet/runtime",
                TargetRepository = "https://github.com/dotnet/aspnetcore",
                TargetBranch = "main",
            }),
            new IngestedSubscription(new SubscriptionYaml
            {
                Id = sharedId,
                Channel = ".NET 9",
                SourceRepository = "https://github.com/dotnet/runtime2",
                TargetRepository = "https://github.com/dotnet/aspnetcore2",
                TargetBranch = "main",
            }),
        };

        // Act & Assert
        var exception = Assert.Throws<IngestionEntityValidationException>(
            () => EntityValidator.ValidateEntityUniqueness(subscriptions));

        exception.Should().NotBeNull();
        exception.Message.Should().Contain("collection contains duplicate Ids");
        exception.Message.Should().Contain("Entity:");
        exception.Message.Should().Contain(sharedId.ToString());
    }

    #endregion

    #region ToString Tests

    [Test]
    public void IngestedSubscription_ToString_ContainsKeyFields()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var subscription = new IngestedSubscription(new SubscriptionYaml
        {
            Id = subscriptionId,
            Channel = ".NET 8",
            SourceRepository = "https://github.com/dotnet/runtime",
            TargetRepository = "https://github.com/dotnet/aspnetcore",
            TargetBranch = "main",
        });

        // Act
        var result = subscription.ToString();

        // Assert
        result.Should().Contain(subscriptionId.ToString());
        result.Should().Contain(".NET 8");
        result.Should().Contain("https://github.com/dotnet/runtime");
        result.Should().Contain("https://github.com/dotnet/aspnetcore");
        result.Should().Contain("main");
    }

    [Test]
    public void IngestedChannel_ToString_ContainsKeyFields()
    {
        // Arrange
        var channel = new IngestedChannel(new ChannelYaml
        {
            Name = ".NET 8",
            Classification = "release",
        });

        // Act
        var result = channel.ToString();

        // Assert
        result.Should().Contain(".NET 8");
    }

    [Test]
    public void IngestedDefaultChannel_ToString_ContainsKeyFields()
    {
        // Arrange
        var defaultChannel = new IngestedDefaultChannel(new DefaultChannelYaml
        {
            Repository = "https://github.com/dotnet/runtime",
            Branch = "main",
            Channel = ".NET 8",
            Enabled = true,
        });

        // Act
        var result = defaultChannel.ToString();

        // Assert
        result.Should().Contain("https://github.com/dotnet/runtime");
        result.Should().Contain("main");
        result.Should().Contain(".NET 8");
    }

    [Test]
    public void IngestedBranchMergePolicies_ToString_ContainsKeyFields()
    {
        // Arrange
        var branchMergePolicy = new IngestedBranchMergePolicies(new BranchMergePoliciesYaml
        {
            Repository = "https://github.com/dotnet/runtime",
            Branch = "main",
            MergePolicies = [new MergePolicyYaml { Name = "AllChecksSuccessful" }],
        });

        // Act
        var result = branchMergePolicy.ToString();

        // Assert
        result.Should().Contain("https://github.com/dotnet/runtime");
        result.Should().Contain("main");
    }

    #endregion
}
