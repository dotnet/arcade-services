// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Shouldly;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using NUnit.Framework;
using ProductConstructionService.ScenarioTests.ObjectHelpers;
using System;
using System.Collections.Generic;

namespace ProductConstructionService.ScenarioTests.Tests;

public class TestHelpersTest
{
    [TestCase("Single asset set", "1.0.0", 0, "Single asset set", "1.0.0")]
    [TestCase("Single asset set", "1.0.0", 1, "Single asset set", "1.0.0")]
    [TestCase("Single asset set", "1.0.0", 2, "Single asset set", "1.0.0")]
    [TestCase("foo", "1.0.0", 0, "foo", "1.0.0")]
    [TestCase("foo", "1.0.0", 1, "foo", "1.0.0")]
    [TestCase("bar", "1.0.0", 0, "bar", "1.0.0")]
    [TestCase("bar", "1.0.0", 1, "bar", "1.0.0")]
    public void GetAssetData_MatchTopLevelAssetName_Works(string assetName1, string version1, int testIndex, 
        string expectedAssetName, string expectedVersion)
    {
        ScenarioTestBase testBase = new ScenarioTestBase();
        var assets = testBase.GetAssetData(assetName1, version1);
        var assetNameToFind = expectedAssetName;
        assets.ShouldNotBeNull();
        assets.ShouldHaveSingleItem();
        assets[0].Name.ShouldBe(assetNameToFind);
        assets[0].Version.ShouldBe(expectedVersion);
    }

    [TestCase("Single asset set", "1.0.0", "2nd asset", "2.0", 0, "Single asset set", "1.0.0")]
    [TestCase("Single asset set", "1.0.0", "2nd asset", "2.0", 1, "2nd asset", "2.0")]
    [TestCase("foo", "1.0.0", "bar", "2.1", 0, "foo", "1.0.0")]
    [TestCase("foo", "1.0.0", "bar", "2.1", 1, "bar", "2.1")]
    public void GetAssetData_MatchWithTwoAssets_Works(string assetName1, string version1,
        string assetName2, string version2, int testIndex,
        string expectedAssetName, string expectedVersion)
    {
        ScenarioTestBase testBase = new ScenarioTestBase();
        var assets = testBase.GetAssetData(assetName1, version1, assetName2, version2);
        assets.ShouldNotBeNull();
        assets.Count.ShouldBe(2);
        int testNumber = testIndex;
        if (testNumber == 0)
        {
            assets[0].Name.ShouldBe(expectedAssetName);
            assets[0].Version.ShouldBe(expectedVersion);
        }
        else
        {
            assets[1].Name.ShouldBe(expectedAssetName);
            assets[1].Version.ShouldBe(expectedVersion);
        }
    }

    [Test]
    public void GetAssetData_MatchWithManyAssets_Works()
    {
        ScenarioTestBase testBase = new ScenarioTestBase();
        var assets = testBase.GetAssetData(
            "asset1", "1.1", 
            "asset2", "1.2",
            "asset3", "1.3",
            "asset4", "1.4");

        assets.ShouldNotBeNull();
        assets.Count.ShouldBe(4);
        
        assets[0].Name.ShouldBe("asset1");
        assets[0].Version.ShouldBe("1.1");

        assets[1].Name.ShouldBe("asset2");
        assets[1].Version.ShouldBe("1.2");

        assets[2].Name.ShouldBe("asset3");
        assets[2].Version.ShouldBe("1.3");

        assets[3].Name.ShouldBe("asset4");
        assets[3].Version.ShouldBe("1.4");
    }

    [Test]
    public void SubscriptionBuilder_WithDefaults_BuildsAValidSubscription()
    {
        var sourceRepo = "https://github.com/dotnet/arcade";
        var targetRepo = "https://github.com/dotnet/runtime";
        var branch = "main";
        var channel = "test-channel";
        var subscriptionId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var frequency = Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.EveryWeek;
        var batchable = false;
        IList<string> mergePolicies = ["Standard"];

        var subscription = SubscriptionBuilder.BuildSubscription(
            sourceRepo, targetRepo, branch, channel, subscriptionId, frequency, batchable, mergePolicies);

        subscription.ShouldNotBeNull();
        subscription.SourceRepository.ShouldBe(sourceRepo);
        subscription.TargetRepository.ShouldBe(targetRepo);
        subscription.TargetBranch.ShouldBe(branch);
        subscription.Channel.ShouldBe(channel);
        subscription.Id.ShouldBe(subscriptionId);
        subscription.Policy.UpdateFrequency.ShouldBe(frequency);
        subscription.Policy.Batchable.ShouldBe(batchable);
        subscription.Policy.MergePolicies[0].ShouldBe(mergePolicies[0]);
    }

    [Test]
    public void SubscriptionBuilder_WithExcludedAssets_BuildsAValidSubscription()
    {
        var sourceRepo = "https://github.com/dotnet/arcade";
        var targetRepo = "https://github.com/dotnet/runtime";
        var branch = "main";
        var channel = "test-channel";
        var subscriptionId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var frequency = Microsoft.DotNet.ProductConstructionService.Client.Models.UpdateFrequency.EveryWeek;
        var batchable = false;
        IList<string> mergePolicies = ["Standard"];
        IList<string> excludedAssets = ["Exclude1", "Exclude2"];

        var subscription = SubscriptionBuilder.BuildSubscription(
            sourceRepo, targetRepo, branch, channel, subscriptionId, frequency, batchable, mergePolicies, null, excludedAssets);

        subscription.ShouldNotBeNull();
        subscription.SourceRepository.ShouldBe(sourceRepo);
        subscription.TargetRepository.ShouldBe(targetRepo);
        subscription.TargetBranch.ShouldBe(branch);
        subscription.Channel.ShouldBe(channel);
        subscription.Id.ShouldBe(subscriptionId);
        subscription.Policy.UpdateFrequency.ShouldBe(frequency);
        subscription.Policy.Batchable.ShouldBe(batchable);
        subscription.Policy.MergePolicies[0].ShouldBe(mergePolicies[0]);
        subscription.Policy.ExcludedAssets.Count.ShouldBe(2);
        subscription.Policy.ExcludedAssets.ShouldContain("Exclude1");
        subscription.Policy.ExcludedAssets.ShouldContain("Exclude2");
    }
}
