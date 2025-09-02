// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Moq;
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

    /// <summary>
    /// Verifies that GetDiff correctly detects removed, updated (string, bool, list), and added keys.
    /// Input:
    /// - Old JSON contains: removed key, updated string/bool/list, and unchanged values.
    /// - New JSON contains: added key, updated values for the same keys, and unchanged values.
    /// Expected:
    /// - Removed key yields a 'Removed' change with null value.
    /// - Updated keys yield 'Updated' changes with the new values.
    /// - Added key yields an 'Added' change with its value.
    /// - Unchanged keys are not present in the result.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetDiff_MixedChanges_ProducesExpectedChangeSet()
    {
        // Arrange
        var oldJson = new Dictionary<string, object>
        {
            ["removed:key"] = "to be removed",
            ["updated:string"] = "old value",
            ["updated:bool"] = false,
            ["updated:list"] = new List<string> { "old1", "old2" },
            ["unchanged:int"] = 42,
            ["unchanged:list"] = new List<string> { "a", "b" }
        };

        var newJson = new Dictionary<string, object>
        {
            ["updated:string"] = "new value",
            ["updated:bool"] = true,
            ["updated:list"] = new List<string> { "new1", "new2" },
            ["unchanged:int"] = 42,
            ["unchanged:list"] = new List<string> { "a", "b" },
            ["added:key"] = "added value"
        };

        // Act
        var oldSimple = new SimpleConfigJson(oldJson);
        var newSimple = new SimpleConfigJson(newJson);
        var changes = oldSimple.GetDiff(newSimple);

        // Assert
        changes.Should().HaveCount(5);

        // Removed
        var removed = changes.Should().ContainSingle(c => c.Name == "removed:key").Subject;
        removed.IsRemoved().Should().BeTrue();
        removed.Value.Should().BeNull();

        // Updated - string
        var updatedString = changes.Should().ContainSingle(c => c.Name == "updated:string").Subject;
        updatedString.IsUpdated().Should().BeTrue();
        updatedString.Value.Should().Be("new value");

        // Updated - bool
        var updatedBool = changes.Should().ContainSingle(c => c.Name == "updated:bool").Subject;
        updatedBool.IsUpdated().Should().BeTrue();
        updatedBool.Value.Should().Be(true);

        // Updated - list
        var updatedList = changes.Should().ContainSingle(c => c.Name == "updated:list").Subject;
        updatedList.IsUpdated().Should().BeTrue();
        updatedList.Value.Should().BeOfType<List<string>>();
        var list = (List<string>)updatedList.Value!;
        list.Should().BeEquivalentTo(new[] { "new1", "new2" });

        // Added
        var added = changes.Should().ContainSingle(c => c.Name == "added:key").Subject;
        added.IsAdded().Should().BeTrue();
        added.Value.Should().Be("added value");
    }

    /// <summary>
    /// Ensures that GetDiff returns an empty list when both inputs are identical.
    /// Input:
    /// - Old and new JSON dictionaries contain identical key-value pairs (including a list with same order).
    /// Expected:
    /// - No changes are detected; the result is empty.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetDiff_IdenticalInputs_ReturnsEmpty()
    {
        // Arrange
        var json = new Dictionary<string, object>
        {
            ["sdk:version"] = "8.0.303",
            ["isRoot"] = true,
            ["frameworks"] = new List<string> { "net8.0", "net6.0" }
        };

        // Act
        var oldSimple = new SimpleConfigJson(json);
        var newSimple = new SimpleConfigJson(new Dictionary<string, object>(json));
        var changes = oldSimple.GetDiff(newSimple);

        // Assert
        changes.Should().BeEmpty();
    }

    /// <summary>
    /// Validates that GetDiff throws an ArgumentException when the same key has different value types.
    /// Input:
    /// - Old JSON has a value of one type.
    /// - New JSON has the same key with a different runtime type (e.g., string vs bool).
    /// Expected:
    /// - An ArgumentException is thrown with the message: "Key {key} value has different types in old and new json".
    /// </summary>
    [TestCase("string value", true, TestName = "GetDiff_TypeMismatch_StringVsBool_Throws")]
    [TestCase(1, "1", TestName = "GetDiff_TypeMismatch_IntVsString_Throws")]
    [TestCaseSource(nameof(TypeMismatchListCases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetDiff_DifferentTypesForSameKey_ThrowsArgumentException(object oldValue, object newValue)
    {
        // Arrange
        const string key = "property";
        var oldJson = new Dictionary<string, object> { [key] = oldValue };
        var newJson = new Dictionary<string, object> { [key] = newValue };

        // Act
        var oldSimple = new SimpleConfigJson(oldJson);
        var newSimple = new SimpleConfigJson(newJson);
        Action act = () => oldSimple.GetDiff(newSimple);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage($"Key {key} value has different types in old and new json");
    }

    /// <summary>
    /// Ensures that list ordering is significant: the same elements in a different order are considered an update.
    /// Input:
    /// - Old JSON list: ["a", "b"]
    /// - New JSON list: ["b", "a"]
    /// Expected:
    /// - An Updated change for the list key with the new list as value.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void GetDiff_ListSameElementsDifferentOrder_IsUpdated()
    {
        // Arrange
        const string key = "frameworks";
        var oldJson = new Dictionary<string, object> { [key] = new List<string> { "a", "b" } };
        var newJson = new Dictionary<string, object> { [key] = new List<string> { "b", "a" } };

        // Act
        var oldSimple = new SimpleConfigJson(oldJson);
        var newSimple = new SimpleConfigJson(newJson);
        var changes = oldSimple.GetDiff(newSimple);

        // Assert
        var change = changes.Should().ContainSingle(c => c.Name == key).Subject;
        change.IsUpdated().Should().BeTrue();
        change.Value.Should().BeOfType<List<string>>();
        var list = (List<string>)change.Value!;
        list.Should().BeEquivalentTo(new[] { "b", "a" });
    }

    private static IEnumerable TypeMismatchListCases()
    {
        yield return new TestCaseData(new List<string> { "a" }, (object)"a")
            .SetName("GetDiff_TypeMismatch_ListVsString_Throws");
    }
}



public class SimpleConfigJsonConstructorTests
{
    /// <summary>
    /// Verifies that the constructor assigns the provided dictionary instance directly to the Dictionary property
    /// without copying. Covers both empty and populated inputs.
    /// </summary>
    /// <param name="input">The input dictionary provided to the constructor.</param>
    [TestCaseSource(nameof(Constructor_AssignsDictionaryReference_SameInstance_Cases))]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_AssignsDictionaryReference_SameInstance(Dictionary<string, object> input)
    {
        // Arrange
        var expected = input;

        // Act
        var simple = new SimpleConfigJson(input);

        // Assert
        simple.Dictionary.Should().BeSameAs(expected);
        simple.Dictionary.Should().HaveCount(expected.Count);
    }

    /// <summary>
    /// Ensures that modifications to the original dictionary after constructing <see cref="SimpleConfigJson"/>
    /// are reflected by the <see cref="SimpleConfigJson.Dictionary"/> property, proving no defensive copy is made.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_ModifyingInputDictionaryAfterConstruction_ReflectsInProperty()
    {
        // Arrange
        var input = new Dictionary<string, object>
        {
            ["a"] = 1
        };
        var simple = new SimpleConfigJson(input);

        // Act
        input["b"] = "two";
        input.Remove("a");

        // Assert
        simple.Dictionary.Should().ContainKey("b");
        simple.Dictionary["b"].Should().Be("two");
        simple.Dictionary.Should().NotContainKey("a");
        simple.Dictionary.Should().HaveCount(1);
    }

    /// <summary>
    /// Validates that the constructor preserves value types stored in the input dictionary (e.g., strings, ints, bools, lists),
    /// and the instances are accessible as-is via the <see cref="SimpleConfigJson.Dictionary"/> property.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void Constructor_PreservesValueTypes_NoConversion()
    {
        // Arrange
        var list = new List<string> { "x", "y" };
        var input = new Dictionary<string, object>
        {
            ["s"] = "text",
            ["i"] = 42,
            ["b"] = true,
            ["l"] = list,
            ["n"] = null
        };

        // Act
        var simple = new SimpleConfigJson(input);

        // Assert
        simple.Dictionary.Should().BeSameAs(input);
        simple.Dictionary["s"].Should().Be("text");
        simple.Dictionary["i"].Should().Be(42);
        simple.Dictionary["b"].Should().Be(true);
        simple.Dictionary["l"].Should().BeSameAs(list);
        simple.Dictionary.Should().ContainKey("n");
        simple.Dictionary["n"].Should().BeNull();
    }

    private static IEnumerable<TestCaseData> Constructor_AssignsDictionaryReference_SameInstance_Cases()
    {
        yield return new TestCaseData(new Dictionary<string, object>())
            .SetName("Constructor_AssignsDictionaryReference_SameInstance_EmptyDictionary");

        yield return new TestCaseData(new Dictionary<string, object>
        {
            ["key"] = "value",
            ["num"] = 123
        })
            .SetName("Constructor_AssignsDictionaryReference_SameInstance_PopulatedDictionary");
    }
}



public class SimpleConfigJsonApplyJsonChangesTests
{
    /// <summary>
    /// Verifies ApplyJsonChanges applies removals, additions, and updates correctly, creates intermediate objects,
    /// preserves existing values, and emits pretty-printed JSON with relaxed escaping (e.g., "<" unescaped).
    /// Input JSON contains nested objects and various value types; changes remove an existing property,
    /// update scalar values, and add new leaf properties including one with a null value.
    /// Expected: Removed property is absent, updated values are changed, added paths created with correct values,
    /// null value is serialized as "null", and "<script>" remains unescaped in the output.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ApplyJsonChanges_RemovalsAdditionsUpdates_ProducesExpectedJson()
    {
        // Arrange
        var original = """
        {
          "sdk": {
            "version": "8.0.303",
            "rollForward": "minor"
          },
          "tools": {
            "dotnet": "8.0.303",
            "runtimes": {
              "dotnet": ["6.0.29"]
            }
          },
          "flag": true,
          "num": 1,
          "obj": { "inner": "value" }
        }
        """;

        var removals = new List<string>
        {
            "sdk:rollForward"
        };

        var additions = new Dictionary<string, JsonVersionProperty>
        {
            ["sdk:newProp"] = new JsonVersionProperty("sdk:newProp", NodeComparisonResult.Added, "hello"),
            ["new:deep:path"] = new JsonVersionProperty("new:deep:path", NodeComparisonResult.Added, "<script>"),
            ["obj:newNull"] = new JsonVersionProperty("obj:newNull", NodeComparisonResult.Added, null)
        };

        var updates = new Dictionary<string, JsonVersionProperty>
        {
            ["flag"] = new JsonVersionProperty("flag", NodeComparisonResult.Updated, false),
            ["num"] = new JsonVersionProperty("num", NodeComparisonResult.Updated, 42),
            ["tools:dotnet"] = new JsonVersionProperty("tools:dotnet", NodeComparisonResult.Updated, "8.0.400"),
            ["obj:inner"] = new JsonVersionProperty("obj:inner", NodeComparisonResult.Updated, "new value")
        };

        var changes = new VersionFileChanges<JsonVersionProperty>(removals, additions, updates);

        // Act
        var result = SimpleConfigJson.ApplyJsonChanges(original, changes);
        var root = JsonNode.Parse(result)!.AsObject();

        // Assert
        // Existing unchanged
        root["sdk"]!["version"]!.GetValue<string>().Should().Be("8.0.303");

        // Removal
        (root["sdk"]!["rollForward"] == null).Should().BeTrue();
        result.Should().NotContain("\"rollForward\"");

        // Updates
        root["flag"]!.GetValue<bool>().Should().BeFalse();
        root["num"]!.GetValue<int>().Should().Be(42);
        root["tools"]!["dotnet"]!.GetValue<string>().Should().Be("8.0.400");
        root["obj"]!["inner"]!.GetValue<string>().Should().Be("new value");

        // Additions
        root["sdk"]!["newProp"]!.GetValue<string>().Should().Be("hello");
        root["new"]!["deep"]!["path"]!.GetValue<string>().Should().Be("<script>");

        // Null value is serialized as "null"
        result.Should().Contain("\"newNull\": null");

        // Relaxed escaping ensures "<script>" remains unescaped in output
        result.Should().Contain("<script>");

        // Pretty-printing (indented output)
        result.Should().Contain(Environment.NewLine);
    }

    /// <summary>
    /// Verifies ApplyJsonChanges throws when asked to remove a path that cannot be navigated,
    /// because the first segment does not exist in the JSON object.
    /// Input JSON contains { "a": { "b": 1 } } and removal path "x:y".
    /// Expected: InvalidOperationException with a message indicating navigation failure at "x".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ApplyJsonChanges_RemoveNonExistingPath_ThrowsInvalidOperationException()
    {
        // Arrange
        var original = """
        { "a": { "b": 1 } }
        """;

        var removals = new List<string> { "x:y" };
        var additions = new Dictionary<string, JsonVersionProperty>();
        var updates = new Dictionary<string, JsonVersionProperty>();

        var changes = new VersionFileChanges<JsonVersionProperty>(removals, additions, updates);

        // Act
        Action act = () => SimpleConfigJson.ApplyJsonChanges(original, changes);

        // Assert
        act.Should()
           .Throw<InvalidOperationException>()
           .WithMessage("Cannot navigate to x in JSON structure.");
    }

    /// <summary>
    /// Verifies ApplyJsonChanges throws JsonException when the input JSON is syntactically invalid.
    /// Input is an unterminated object literal "{"
    /// Expected: JsonException is thrown from JsonNode.Parse.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ApplyJsonChanges_InvalidJson_ThrowsJsonException()
    {
        // Arrange
        var invalid = "{";

        var changes = new VersionFileChanges<JsonVersionProperty>(
            new List<string>(),
            new Dictionary<string, JsonVersionProperty>(),
            new Dictionary<string, JsonVersionProperty>());

        // Act
        Action act = () => SimpleConfigJson.ApplyJsonChanges(invalid, changes);

        // Assert
        act.Should().Throw<JsonException>();
    }

    /// <summary>
    /// Verifies ApplyJsonChanges throws InvalidOperationException when input JSON is the literal 'null',
    /// which parses to a null JsonNode and triggers the explicit guard.
    /// Expected: InvalidOperationException with message "Failed to parse JSON file."
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ApplyJsonChanges_NullJsonRoot_ThrowsInvalidOperationException()
    {
        // Arrange
        var nullJson = "null";

        var changes = new VersionFileChanges<JsonVersionProperty>(
            new List<string>(),
            new Dictionary<string, JsonVersionProperty>(),
            new Dictionary<string, JsonVersionProperty>());

        // Act
        Action act = () => SimpleConfigJson.ApplyJsonChanges(nullJson, changes);

        // Assert
        act.Should()
           .Throw<InvalidOperationException>()
           .WithMessage("Failed to parse JSON file.");
    }

    /// <summary>
    /// Verifies that attempting to add a nested property under an existing non-object node
    /// (e.g., path "flag:inner" when "flag" is a boolean) does not throw and does not modify that path.
    /// Input JSON contains "flag": true and change attempts to add "flag:inner".
    /// Expected: No exception; the "flag" property remains a boolean and "inner" is not added.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void ApplyJsonChanges_AddNestedUnderNonObject_DoesNotModifyAndNoException()
    {
        // Arrange
        var original = """
        { "flag": true }
        """;

        var removals = new List<string>();
        var additions = new Dictionary<string, JsonVersionProperty>
        {
            ["flag:inner"] = new JsonVersionProperty("flag:inner", NodeComparisonResult.Added, "x")
        };
        var updates = new Dictionary<string, JsonVersionProperty>();

        var changes = new VersionFileChanges<JsonVersionProperty>(removals, additions, updates);

        // Act
        var result = SimpleConfigJson.ApplyJsonChanges(original, changes);
        var root = JsonNode.Parse(result)!.AsObject();

        // Assert
        root["flag"]!.GetValue<bool>().Should().BeTrue();
        result.Should().NotContain("\"inner\"");
    }
}
