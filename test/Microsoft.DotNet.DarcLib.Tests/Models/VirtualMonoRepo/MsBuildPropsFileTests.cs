#nullable disable
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using FluentAssertions;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests.Models.VirtualMonoRepo;


[TestFixture]
public class MsBuildPropsFileTests
{
    private class PropsFile : MsBuildPropsFile
    {
        public Dictionary<string, string> Properties;
        public PropsFile(bool? orderPropertiesAscending, Dictionary<string, string> properties) : base(orderPropertiesAscending)
        {
            Properties = properties;
        }

        protected override void SerializeProperties(XmlElement propertyGroup, Func<string, XmlElement> createElement) => SerializeDictionary(Properties, propertyGroup, createElement);
        public static Dictionary<string, string> Deserialize(string path) => DeserializeProperties(path);
    }

    [SetUp]
    public void SetUpOutputFile()
    {
        _outputFile = Path.GetTempFileName();
    }

    [TearDown]
    public void CleanUpOutputFile()
    {
        try
        {
            if (_outputFile is not null)
            {
                File.Delete(_outputFile);
            }
        }
        catch
        {
            // Ignore
        }
    }

    [Test]
    public void MsBuildPropsFileIsDeSerializedTest()
    {
        var runtimeVersion = new VmrDependencyVersion("26a71c61fbda229f151afb14e274604b4926df5c");
        var sdkVersion = new VmrDependencyVersion("6e00e543bbeb8e0491420e2f6b3f7d235166596d");

        var propsFile = new PropsFile(
            true,
            new Dictionary<string, string>
            {
                { "sdkGitCommitHash", sdkVersion.Sha },
                { "runtimeGitCommitHash", runtimeVersion.Sha },
            });

        propsFile.SerializeToXml(_outputFile ?? throw new Exception("Output file is not initialized"));

        var content = File.ReadAllText(_outputFile);
        content.Trim().Should().Be(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <runtimeGitCommitHash>{runtimeVersion.Sha}</runtimeGitCommitHash>
                <sdkGitCommitHash>{sdkVersion.Sha}</sdkGitCommitHash>
              </PropertyGroup>
            </Project>
            """);

        var properties = PropsFile.Deserialize(_outputFile);
        properties["runtimeGitCommitHash"].Should().Be(runtimeVersion.Sha);
        properties["sdkGitCommitHash"].Should().Be(sdkVersion.Sha);
    }

    private class TestPropsFile : MsBuildPropsFile
    {
        private readonly Dictionary<string, string> _properties;

        public TestPropsFile(bool? orderPropertiesAscending, Dictionary<string, string> properties)
            : base(orderPropertiesAscending)
        {
            _properties = properties;
        }

        protected override void SerializeProperties(XmlElement propertyGroup, Func<string, XmlElement> createElement)
        {
            SerializeDictionary(_properties, propertyGroup, createElement);
        }
    }

    /// <summary>
    /// Verifies that when the provided path does not include a directory component (e.g., just a file name),
    /// SerializeToXml throws an ArgumentException indicating the path is invalid.
    /// Inputs:
    ///  - path: "file.props" (no directory).
    /// Expected:
    ///  - ArgumentException is thrown with the message "'file.props' is not a valid path.".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeToXml_PathWithoutDirectory_ThrowsArgumentExceptionWithMessage()
    {
        // Arrange
        var props = new Dictionary<string, string> { { "A", "1" } };
        var sut = new TestPropsFile(orderPropertiesAscending: null, properties: props);
        var invalidPath = "file.props";

        // Act
        Action act = () => sut.SerializeToXml(invalidPath);

        // Assert
        act.Should().Throw<ArgumentException>()
           .WithMessage($"'{invalidPath}' is not a valid path.");
    }

    /// <summary>
    /// Ensures that when the target directory does not exist, SerializeToXml creates it and writes a valid XML file
    /// containing the Project and PropertyGroup elements with the specified properties.
    /// Inputs:
    ///  - path: a file path pointing to a non-existent directory.
    ///  - properties: { "A": "1", "B": "2" }.
    /// Expected:
    ///  - The directory is created.
    ///  - The file exists.
    ///  - The XML root is "Project" and contains a "PropertyGroup" with elements "A" and "B" having values "1" and "2".
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeToXml_DirectoryMissing_CreatesDirectoryAndWritesExpectedXml()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), "MsBuildPropsFileTests", Guid.NewGuid().ToString("N"));
        var targetDir = Path.Combine(tempRoot, "sub");
        var path = Path.Combine(targetDir, "props.props");

        var properties = new Dictionary<string, string>
            {
                { "A", "1" },
                { "B", "2" },
            };
        var sut = new TestPropsFile(orderPropertiesAscending: null, properties: properties);

        try
        {
            // Act
            sut.SerializeToXml(path);

            // Assert
            Directory.Exists(targetDir).Should().BeTrue("target directory should be created when missing");
            File.Exists(path).Should().BeTrue("XML should be written to the specified path");

            var doc = XDocument.Load(path);
            doc.Root.Should().NotBeNull("XML must have a root element");
            doc.Root.Name.LocalName.Should().Be("Project", "root element should be <Project>");

            var propertyGroup = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "PropertyGroup");
            propertyGroup.Should().NotBeNull("XML should contain a <PropertyGroup> element");

            var a = propertyGroup.Elements().FirstOrDefault(e => e.Name.LocalName == "A");
            var b = propertyGroup.Elements().FirstOrDefault(e => e.Name.LocalName == "B");

            a.Should().NotBeNull("Property 'A' should be serialized");
            b.Should().NotBeNull("Property 'B' should be serialized");
            a.Value.Should().Be("1");
            b.Value.Should().Be("2");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// Validates that when the target file already exists, SerializeToXml overwrites it rather than appending,
    /// and produces a valid XML structure.
    /// Inputs:
    ///  - path: existing file containing sentinel text.
    ///  - properties: { "X": "42" }.
    /// Expected:
    ///  - The file content no longer contains the sentinel.
    ///  - The file contains a valid <Project> XML with a <PropertyGroup><X>42</X></PropertyGroup>.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeToXml_ExistingFile_IsOverwrittenWithValidXml()
    {
        // Arrange
        var tempRoot = Path.Combine(Path.GetTempPath(), "MsBuildPropsFileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var path = Path.Combine(tempRoot, "existing.props");
        var sentinel = "OLDCONTENT_12345";
        File.WriteAllText(path, sentinel, Encoding.UTF8);

        var properties = new Dictionary<string, string> { { "X", "42" } };
        var sut = new TestPropsFile(orderPropertiesAscending: true, properties: properties);

        try
        {
            // Act
            sut.SerializeToXml(path);

            // Assert
            var content = File.ReadAllText(path, Encoding.UTF8);
            content.Should().NotContain(sentinel, "existing file must be overwritten, not appended");
            content.Should().Contain("<Project");
            content.Should().Contain("PropertyGroup");
            content.Should().Contain("<X>42</X>");

            var doc = XDocument.Load(path);
            doc.Root.Name.LocalName.Should().Be("Project");
            var x = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "X");
            x.Should().NotBeNull();
            x.Value.Should().Be("42");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// Verifies that the constructor's parameter 'orderPropertiesAscending' controls the ordering behavior
    /// of property serialization via SerializeDictionary.
    /// Inputs:
    ///  - orderPropertiesAscending: true, false, or null (keep insertion order).
    ///  - Properties inserted in order: "b", "a", "c".
    /// Expected:
    ///  - true  -> ascending:  ["a","b","c"]
    ///  - false -> descending: ["c","b","a"]
    ///  - null  -> insertion:  ["b","a","c"]
    /// </summary>
    [TestCase(true, "a,b,c")]
    [TestCase(false, "c,b,a")]
    [TestCase(null, "b,a,c")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void MsBuildPropsFile_Ctor_OrderControlsDictionarySerialization(bool? orderAscending, string expectedOrderCsv)
    {
        // Arrange
        var properties = new Dictionary<string, string>
        {
            { "b", "2" },
            { "a", "1" },
            { "c", "3" },
        };
        var expected = expectedOrderCsv.Split(',');

        var sut = new TestablePropsFile(orderAscending);

        // Act
        var actual = sut.CaptureSerializedOrder(properties);

        // Assert
        actual.Should().Equal(expected);
    }

    private sealed class TestablePropsFile : MsBuildPropsFile
    {
        public TestablePropsFile(bool? orderPropertiesAscending) : base(orderPropertiesAscending) { }

        protected override void SerializeProperties(XmlElement propertyGroup, Func<string, XmlElement> createElement)
        {
            // Not used in this test; constructor behavior is validated via SerializeDictionary.
        }

        public IList<string> CaptureSerializedOrder(Dictionary<string, string> properties)
        {
            var doc = new XmlDocument();
            var group = doc.CreateElement("PropertyGroup");

            SerializeDictionary(properties, group, doc.CreateElement);

            return group.ChildNodes
                .OfType<XmlElement>()
                .Select(e => e.Name)
                .ToList();
        }
    }

    /// <summary>
    /// Ensures the string-based SerializeToXml uses a TextWriter underneath, resulting in an XML declaration
    /// that declares UTF-16 encoding (TextWriter-based XmlWriter ignores the Encoding setting).
    /// Inputs:
    ///  - A simple props file with one property.
    /// Expected:
    ///  - The returned XML string contains 'encoding="utf-16"' in the XML declaration.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeToXml_UsingStringWriter_ProducesUtf16XmlDeclaration()
    {
        // Arrange
        var props = new Dictionary<string, string> { { "Key", "Value" } };
        var file = new PropsFile(true, props);

        // Act
        var xml = file.SerializeToXml();

        // Assert
        xml.Should().Contain(@"encoding=""utf-16""");
    }

    /// <summary>
    /// Validates that property ordering in the returned XML matches the configuration:
    ///  - true: ascending order by key
    ///  - false: descending order by key
    ///  - null: insertion order from the dictionary
    /// Inputs:
    ///  - A dictionary inserted in order: "b", "a".
    /// Expected:
    ///  - The element order under PropertyGroup equals the provided expected order.
    /// </summary>
    [TestCase(true, "a,b")]
    [TestCase(false, "b,a")]
    [TestCase(null, "b,a")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeToXml_PropertyOrder_RespectsConfiguration(bool? orderAscending, string expectedOrderCsv)
    {
        // Arrange
        var props = new Dictionary<string, string>
        {
            { "b", "2" },
            { "a", "1" },
        };
        var file = new PropsFile(orderAscending, props);
        var expectedOrder = expectedOrderCsv.Split(',').Select(s => s.Trim()).ToArray();

        // Act
        var xml = file.SerializeToXml();
        var xdoc = XDocument.Parse(xml);
        var orderedNames = xdoc
            .Element("Project")
            .Element("PropertyGroup")
            .Elements()
            .Select(e => e.Name.LocalName)
            .ToArray();

        // Assert
        orderedNames.Should().Equal(expectedOrder);
    }

    /// <summary>
    /// Ensures that special XML characters in property values are correctly escaped during serialization
    /// and round-trip back to the original values when parsed.
    /// Inputs:
    ///  - A property value containing &, <, >, ", ' characters.
    /// Expected:
    ///  - Parsing the returned XML yields the exact original value for that element.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeToXml_SpecialCharactersInValues_EscapedAndRoundTrip()
    {
        // Arrange
        var specialValue = "a<&>\"'b";
        var props = new Dictionary<string, string>
        {
            { "Special", specialValue }
        };
        var file = new PropsFile(true, props);

        // Act
        var xml = file.SerializeToXml();
        var xdoc = XDocument.Parse(xml);
        var roundTripped = xdoc
            .Element("Project")
            .Element("PropertyGroup")
            .Element("Special")
            .Value;

        // Assert
        roundTripped.Should().Be(specialValue);
    }

    private class TestableMsBuildPropsFile : MsBuildPropsFile
    {
        public TestableMsBuildPropsFile() : base(null) { }

        protected override void SerializeProperties(XmlElement propertyGroup, Func<string, XmlElement> createElement)
        {
            // Not required for DeserializeProperties tests.
        }

        public static Dictionary<string, string> Deserialize(string path) => DeserializeProperties(path);
    }

    /// <summary>
    /// Verifies that elements under multiple PropertyGroup nodes are flattened into a single dictionary.
    /// Inputs:
    ///  - A .props XML with <Project> containing two <PropertyGroup> nodes with unique property names.
    /// Expected:
    ///  - Dictionary with keys A, B, and C mapped to their text values.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeserializeProperties_ProjectAndMultiplePropertyGroups_FlattensPropertiesIntoDictionary()
    {
        // Arrange
        var xml = """
                      <Project>
                        <PropertyGroup>
                          <A>1</A>
                        </PropertyGroup>
                        <PropertyGroup>
                          <B>2</B>
                          <C>3</C>
                        </PropertyGroup>
                      </Project>
                      """;
        var path = CreateTempFile(xml);

        try
        {
            // Act
            var result = TestableMsBuildPropsFile.Deserialize(path);

            // Assert
            result.Should().BeEquivalentTo(new Dictionary<string, string>
                {
                    { "A", "1" },
                    { "B", "2" },
                    { "C", "3" },
                });
        }
        finally
        {
            SafeDelete(path);
        }
    }

    /// <summary>
    /// Validates that when the XML lacks the required PropertyGroup structure, an empty dictionary is returned.
    /// Inputs:
    ///  - A .props XML with <Project> but without any <PropertyGroup>.
    /// Expected:
    ///  - Empty dictionary (no properties found).
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeserializeProperties_ProjectWithoutPropertyGroup_ReturnsEmptyDictionary()
    {
        // Arrange
        var xml = """
                      <Project>
                        <ItemGroup>
                          <SomeItem>ignored</SomeItem>
                        </ItemGroup>
                      </Project>
                      """;
        var path = CreateTempFile(xml);

        try
        {
            // Act
            var result = TestableMsBuildPropsFile.Deserialize(path);

            // Assert
            result.Should().BeEmpty();
        }
        finally
        {
            SafeDelete(path);
        }
    }

    /// <summary>
    /// Ensures that duplicate property names across PropertyGroup elements cause an exception due to duplicate keys.
    /// Inputs:
    ///  - A .props XML with two <PropertyGroup> nodes each containing the same element <A>.
    /// Expected:
    ///  - ArgumentException is thrown by ToDictionary when adding a duplicate key.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeserializeProperties_DuplicatePropertyNames_ThrowsArgumentException()
    {
        // Arrange
        var xml = """
                      <Project>
                        <PropertyGroup>
                          <A>1</A>
                        </PropertyGroup>
                        <PropertyGroup>
                          <A>2</A>
                        </PropertyGroup>
                      </Project>
                      """;
        var path = CreateTempFile(xml);

        try
        {
            // Act
            Action act = () => TestableMsBuildPropsFile.Deserialize(path);

            // Assert
            act.Should().Throw<ArgumentException>();
        }
        finally
        {
            SafeDelete(path);
        }
    }

    /// <summary>
    /// Verifies behavior when the input path does not exist.
    /// Inputs:
    ///  - A non-existent file path.
    /// Expected:
    ///  - FileNotFoundException thrown by XDocument.Load.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeserializeProperties_FileDoesNotExist_ThrowsFileNotFoundException()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".props");

        // Act
        Action act = () => TestableMsBuildPropsFile.Deserialize(path);

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    /// <summary>
    /// Validates that elements under a default XML namespace are not matched by name-only descendant queries,
    /// resulting in no properties found.
    /// Inputs:
    ///  - A .props XML with <Project> and <PropertyGroup> in the default MSBuild namespace.
    /// Expected:
    ///  - Empty dictionary, because Descendants("Project") and Descendants("PropertyGroup") do not match namespaced elements.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeserializeProperties_ProjectWithDefaultNamespace_NoElementsMatchedReturnsEmptyDictionary()
    {
        // Arrange
        var xml = """
                      <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                        <PropertyGroup>
                          <A>1</A>
                        </PropertyGroup>
                      </Project>
                      """;
        var path = CreateTempFile(xml);

        try
        {
            // Act
            var result = TestableMsBuildPropsFile.Deserialize(path);

            // Assert
            result.Should().BeEmpty();
        }
        finally
        {
            SafeDelete(path);
        }
    }

    /// <summary>
    /// Ensures that namespace prefixes on properties are ignored and the local element name is used as the dictionary key.
    /// Inputs:
    ///  - A .props XML with a namespaced element <ns:Version> alongside a standard element.
    /// Expected:
    ///  - Dictionary contains keys "Version" and "Name" with their respective text values.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void DeserializeProperties_NamespacePrefixedProperty_UsesLocalNameAsDictionaryKey()
    {
        // Arrange
        var xml = """
                      <Project>
                        <PropertyGroup>
                          <ns:Version xmlns:ns="urn:x">1.2.3</ns:Version>
                          <Name>LibraryX</Name>
                        </PropertyGroup>
                      </Project>
                      """;
        var path = CreateTempFile(xml);

        try
        {
            // Act
            var result = TestableMsBuildPropsFile.Deserialize(path);

            // Assert
            result.Should().BeEquivalentTo(new Dictionary<string, string>
                {
                    { "Version", "1.2.3" },
                    { "Name", "LibraryX" },
                });
        }
        finally
        {
            SafeDelete(path);
        }
    }

    private static string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".props");
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    /// <summary>
    /// Verifies that SerializeDictionary orders keys and appends elements according to _orderPropertiesAscending.
    /// Inputs:
    ///  - properties inserted in order: b, a, c with values "B", "A", "C".
    ///  - mode controls order: "asc" => ascending, "desc" => descending, "none" => insertion order.
    /// Expected:
    ///  - Child element names in propertyGroup are appended in expected order.
    ///  - Each element's InnerText equals the corresponding value from the dictionary.
    /// </summary>
    [Test]
    [TestCase("asc", "a,b,c")]
    [TestCase("desc", "c,b,a")]
    [TestCase("none", "b,a,c")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeDictionary_OrderingModes_AppendsElementsInExpectedOrder(string mode, string expectedOrderCsv)
    {
        // Arrange
        var properties = new Dictionary<string, string>
        {
            { "b", "B" },
            { "a", "A" },
            { "c", "C" },
        };

        Nullable<bool> order = mode == "asc"
            ? new Nullable<bool>(true)
            : mode == "desc"
                ? new Nullable<bool>(false)
                : new Nullable<bool>();

        var xmlDoc = new XmlDocument();
        var propertyGroup = xmlDoc.CreateElement("PropertyGroup");
        Func<string, XmlElement> createElement = k => xmlDoc.CreateElement(k);

        var sut = new TestableMsBuildPropsFile(order);

        // Act
        sut.InvokeSerializeDictionary(properties, propertyGroup, createElement);

        // Assert
        var names = new List<string>();
        for (int i = 0; i < propertyGroup.ChildNodes.Count; i++)
        {
            names.Add(((XmlElement)propertyGroup.ChildNodes[i]).Name);
        }

        string actualOrder = string.Join(",", names);
        actualOrder.Should().Be(expectedOrderCsv);

        for (int i = 0; i < propertyGroup.ChildNodes.Count; i++)
        {
            var child = (XmlElement)propertyGroup.ChildNodes[i];
            child.InnerText.Should().Be(properties[child.Name]);
        }
    }

    /// <summary>
    /// Ensures that when the dictionary is empty, no elements are appended regardless of ordering mode.
    /// Inputs:
    ///  - Empty properties dictionary.
    ///  - mode controls order: "asc", "desc", or "none".
    /// Expected:
    ///  - propertyGroup contains no child nodes after serialization.
    /// </summary>
    [Test]
    [TestCase("asc")]
    [TestCase("desc")]
    [TestCase("none")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeDictionary_EmptyDictionary_NoChildrenAppended(string mode)
    {
        // Arrange
        var properties = new Dictionary<string, string>();

        Nullable<bool> order = mode == "asc"
            ? new Nullable<bool>(true)
            : mode == "desc"
                ? new Nullable<bool>(false)
                : new Nullable<bool>();

        var xmlDoc = new XmlDocument();
        var propertyGroup = xmlDoc.CreateElement("PropertyGroup");
        Func<string, XmlElement> createElement = k => xmlDoc.CreateElement(k);

        var sut = new TestableMsBuildPropsFile(order);

        // Act
        sut.InvokeSerializeDictionary(properties, propertyGroup, createElement);

        // Assert
        propertyGroup.ChildNodes.Count.Should().Be(0);
    }

    /// <summary>
    /// Validates that values with special characters and whitespace are preserved in InnerText.
    /// Inputs:
    ///  - A single key/value pair where the value may be empty, whitespace, or contain XML special characters.
    /// Expected:
    ///  - The appended element's InnerText exactly equals the input value.
    /// </summary>
    [Test]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("<>&'\"")]
    [TestCase("line1\nline2\r\nline3\tend")]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeDictionary_SpecialValueCharacters_PreservedInInnerText(string value)
    {
        // Arrange
        var properties = new Dictionary<string, string>
        {
            { "name", value },
        };

        var xmlDoc = new XmlDocument();
        var propertyGroup = xmlDoc.CreateElement("PropertyGroup");
        Func<string, XmlElement> createElement = k => xmlDoc.CreateElement(k);

        var sut = new TestableMsBuildPropsFile(new Nullable<bool>(true));

        // Act
        sut.InvokeSerializeDictionary(properties, propertyGroup, createElement);

        // Assert
        propertyGroup.ChildNodes.Count.Should().Be(1);
        var child = (XmlElement)propertyGroup.ChildNodes[0];
        child.Name.Should().Be("name");
        child.InnerText.Should().Be(value);
    }

    /// <summary>
    /// Confirms that very long string values are handled without truncation or corruption.
    /// Inputs:
    ///  - A single key with a 2048-character string value.
    /// Expected:
    ///  - The appended element's InnerText equals the long input string.
    /// </summary>
    [Test]
    [Author("Code Testing Agent v0.3.0-alpha.25425.8+159f94d")]
    [Category("auto-generated")]
    public void SerializeDictionary_VeryLongValue_PreservedCompletely()
    {
        // Arrange
        string longValue = new string('x', 2048);
        var properties = new Dictionary<string, string>
        {
            { "long", longValue },
        };

        var xmlDoc = new XmlDocument();
        var propertyGroup = xmlDoc.CreateElement("PropertyGroup");
        Func<string, XmlElement> createElement = k => xmlDoc.CreateElement(k);

        var sut = new TestableMsBuildPropsFile(new Nullable<bool>(false));

        // Act
        sut.InvokeSerializeDictionary(properties, propertyGroup, createElement);

        // Assert
        propertyGroup.ChildNodes.Count.Should().Be(1);
        var child = (XmlElement)propertyGroup.ChildNodes[0];
        child.Name.Should().Be("long");
        child.InnerText.Should().Be(longValue);
    }
}

