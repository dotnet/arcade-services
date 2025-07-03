// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers;

public class FlatJsonComparerTests
{
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
        var changes = FlatJsonComparer.CompareFlatJsons(oldJson, newJson);

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
        var changes = FlatJsonComparer.CompareFlatJsons(json, new Dictionary<string, object>(json));

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
        var action = () => FlatJsonComparer.CompareFlatJsons(oldJson, newJson);
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
        var action = () => FlatJsonComparer.CompareFlatJsons(oldJson, newJson);
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
        var changes = FlatJsonComparer.CompareFlatJsons(oldJson, newJson);

        // Assert
        changes.Should().BeEmpty();
    }
}
