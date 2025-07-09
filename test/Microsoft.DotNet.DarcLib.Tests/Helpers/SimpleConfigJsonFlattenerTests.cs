// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Helpers;

public class SimpleConfigJsonFlattenerTests
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
        var result = SimpleConfigJsonFlattener.FlattenSimpleConfigJsonToDictionary(json);

        // Assert
        result.Should().HaveCount(12);
        
        // Test string values
        result["sdk:version"].Should().Be("8.0.303");
        result["sdk:rollForward"].Should().Be("minor");
        result["tools:dotnet"].Should().Be("8.0.303");
        result["msbuild-sdks:Microsoft.DotNet.Arcade.Sdk"].Should().Be("8.0.0-beta.25326.1");
        result["nested:deep:value"].Should().Be("deeply nested");
        
        // Test numeric values (flattened as strings)
        result["version"].Should().Be("1");
        
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
}
