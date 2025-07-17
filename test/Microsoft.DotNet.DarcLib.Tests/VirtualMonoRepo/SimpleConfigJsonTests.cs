// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.VirtualMonoRepo;

public class SimpleConfigJsonTests
{
    [Test]
    public void FlattenJsonToDictionary_AllScenarios_FlattensCorrectly()
    {
        // Arrange - comprehensive JSON with all scenarios from global.json and dotnet-tools.json
        var json = """
        {
          "sdk": {
            "version": "8.0.303",
            "rollForward": "minor"
          },
          "tools": {
            "dotnet": "8.0.303",
            "runtimes": {
              "dotnet": ["6.0.29"],
              "aspnetcore": ["6.0.29", "7.0.0"]
            }
          },
          "msbuild-sdks": {
            "Microsoft.DotNet.Arcade.Sdk": "8.0.0-beta.25326.1"
          },
          "version": 1,
          "isRoot": true,
          "enabled": false,
          "nullValue": null,
          "emptyArray": [],
          "nested": {
            "deep": {
              "value": "deeply nested"
            }
          }
        }
        """;

        // Act
        var simpleConfigJson = SimpleConfigJson.Parse(json);
        var result = simpleConfigJson.Dictionary;

        // Assert
        result.Should().HaveCount(12);
        
        // Test string values
        result["sdk:version"].Should().Be("8.0.303");
        result["sdk:rollForward"].Should().Be("minor");
        result["tools:dotnet"].Should().Be("8.0.303");
        result["msbuild-sdks:Microsoft.DotNet.Arcade.Sdk"].Should().Be("8.0.0-beta.25326.1");
        result["nested:deep:value"].Should().Be("deeply nested");
        
        result["version"].Should().Be(1);
        
        // Test boolean values (preserved as booleans)
        result["isRoot"].Should().BeOfType<bool>().And.Be(true);
        result["enabled"].Should().BeOfType<bool>().And.Be(false);
        
        // Test null values (flattened as empty strings)
        result["nullValue"].Should().Be("");
        
        // Test arrays (flattened as List<string>)
        result["tools:runtimes:dotnet"].Should().BeOfType<List<string>>();
        var dotnetRuntimes = (List<string>)result["tools:runtimes:dotnet"];
        dotnetRuntimes.Should().HaveCount(1);
        dotnetRuntimes[0].Should().Be("6.0.29");

        result["tools:runtimes:aspnetcore"].Should().BeOfType<List<string>>();
        var aspnetRuntimes = (List<string>)result["tools:runtimes:aspnetcore"];
        aspnetRuntimes.Should().HaveCount(2);
        aspnetRuntimes[0].Should().Be("6.0.29");
        aspnetRuntimes[1].Should().Be("7.0.0");
        
        // Test empty arrays
        result["emptyArray"].Should().BeOfType<List<string>>();
        var emptyArray = (List<string>)result["emptyArray"];
        emptyArray.Should().BeEmpty();
    }

    [Test]
    public void CompareFlatJsons_AllScenarios_DetectsAllChanges()
    {
        // Arrange
        var oldJson = new Dictionary<string, object>
        {
            ["sdk:version"] = "8.0.303",
            ["tools:dotnet"] = "8.0.303",
            ["msbuild-sdks:Microsoft.DotNet.Arcade.Sdk"] = "8.0.0-beta.25326.1",
            ["isRoot"] = true,
            ["frameworks"] = new List<string> { "net8.0", "net6.0" },
            ["removed:property"] = "will be removed",
            ["updated:string"] = "old value",
            ["updated:bool"] = false,
            ["updated:array"] = new List<string> { "old1", "old2" }
        };

        var newJson = new Dictionary<string, object>
        {
            ["sdk:version"] = "8.0.303", // unchanged
            ["tools:dotnet"] = "8.0.400", // updated
            ["msbuild-sdks:Microsoft.DotNet.Arcade.Sdk"] = "8.0.0-beta.25326.1", // unchanged
            ["isRoot"] = true, // unchanged
            ["frameworks"] = new List<string> { "net8.0", "net6.0" }, // unchanged
            // removed:property is missing (removed)
            ["updated:string"] = "new value", // updated
            ["updated:bool"] = true, // updated
            ["updated:array"] = new List<string> { "new1", "new2" }, // updated
            ["added:property"] = "new property" // added
        };

        // Act
        var oldSimpleConfigJson = new SimpleConfigJson(oldJson);
        var newSimpleConfigJson = new SimpleConfigJson(newJson);
        var changes = oldSimpleConfigJson.GetDiff(newSimpleConfigJson);

        // Assert
        changes.Should().HaveCount(6);

        // Check removed property
        var removedChange = changes.Should().ContainSingle(c => c.Name == "removed:property").Subject;
        removedChange.IsRemoved().Should().BeTrue();
        removedChange.Value.Should().BeNull();

        // Check updated string
        var updatedStringChange = changes.Should().ContainSingle(c => c.Name == "updated:string").Subject;
        updatedStringChange.IsUpdated().Should().BeTrue();
        updatedStringChange.Value.Should().Be("new value");

        // Check updated boolean
        var updatedBoolChange = changes.Should().ContainSingle(c => c.Name == "updated:bool").Subject;
        updatedBoolChange.IsUpdated().Should().BeTrue();
        updatedBoolChange.Value.Should().Be(true);

        // Check updated array
        var updatedArrayChange = changes.Should().ContainSingle(c => c.Name == "updated:array").Subject;
        updatedArrayChange.IsUpdated().Should().BeTrue();
        updatedArrayChange.Value.Should().BeOfType<List<string>>();
        var updatedArray = (List<string>)updatedArrayChange.Value!;
        updatedArray.Should().BeEquivalentTo(new[] { "new1", "new2" });

        // Check added property
        var addedChange = changes.Should().ContainSingle(c => c.Name == "added:property").Subject;
        addedChange.IsAdded().Should().BeTrue();
        addedChange.Value.Should().Be("new property");

        // Check updated tools:dotnet
        var updatedToolsChange = changes.Should().ContainSingle(c => c.Name == "tools:dotnet").Subject;
        updatedToolsChange.IsUpdated().Should().BeTrue();
        updatedToolsChange.Value.Should().Be("8.0.400");
    }

    [Test]
    public void CompareFlatJsons_IdenticalJsons_ReturnsEmptyChanges()
    {
        // Arrange
        var json = new Dictionary<string, object>
        {
            ["sdk:version"] = "8.0.303",
            ["isRoot"] = true,
            ["frameworks"] = new List<string> { "net8.0", "net6.0" }
        };

        // Act
        var oldSimpleConfigJson = new SimpleConfigJson(json);
        var newSimpleConfigJson = new SimpleConfigJson(new Dictionary<string, object>(json));
        var changes = oldSimpleConfigJson.GetDiff(newSimpleConfigJson);

        // Assert
        changes.Should().BeEmpty();
    }

    [Test]
    public void CompareFlatJsons_DifferentTypesForSameKey_ThrowsArgumentException()
    {
        // Arrange
        var oldJson = new Dictionary<string, object>
        {
            ["property"] = "string value"
        };

        var newJson = new Dictionary<string, object>
        {
            ["property"] = true // different type
        };

        // Act & Assert
        var oldSimpleConfigJson = new SimpleConfigJson(oldJson);
        var newSimpleConfigJson = new SimpleConfigJson(newJson);
        var action = () => oldSimpleConfigJson.GetDiff(newSimpleConfigJson);
        action.Should().Throw<ArgumentException>()
            .WithMessage("Key property value has different types in old and new json");
    }

    [Test]
    public void CompareFlatJsons_DifferentListTypesForSameKey_ThrowsArgumentException()
    {
        // Arrange
        var oldJson = new Dictionary<string, object>
        {
            ["property"] = new List<string> { "value1" }
        };

        var newJson = new Dictionary<string, object>
        {
            ["property"] = "string value" // different type
        };

        // Act & Assert
        var oldSimpleConfigJson = new SimpleConfigJson(oldJson);
        var newSimpleConfigJson = new SimpleConfigJson(newJson);
        var action = () => oldSimpleConfigJson.GetDiff(newSimpleConfigJson);
        action.Should().Throw<ArgumentException>()
            .WithMessage("Key property value has different types in old and new json");
    }

    [Test]
    public void CompareFlatJsons_EmptyJsons_ReturnsEmptyChanges()
    {
        // Arrange
        var oldJson = new Dictionary<string, object>();
        var newJson = new Dictionary<string, object>();

        // Act
        var oldSimpleConfigJson = new SimpleConfigJson(oldJson);
        var newSimpleConfigJson = new SimpleConfigJson(newJson);
        var changes = oldSimpleConfigJson.GetDiff(newSimpleConfigJson);

        // Assert
        changes.Should().BeEmpty();
    }
}
