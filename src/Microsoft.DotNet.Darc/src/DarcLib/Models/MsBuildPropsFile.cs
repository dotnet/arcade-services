// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Models;

public interface IMsBuildPropsFile
{
    void SerializeToXml(string path);
}

/// <summary>
/// Represents a regular MSBuild .props file with arbitrary data that can de/serialize to XML.
/// Example:
/// <![CDATA[
///    <Project>
///      <PropertyGroup>
///        -> List of properties comes here <-
///      </PropertyGroup>
///    </Project>
/// ]]>
/// </summary>
public abstract class MsBuildPropsFile : IMsBuildPropsFile
{
    private const string ProjectPropertyName = "Project";
    private const string PropertyGroupName = "PropertyGroup";

    public void SerializeToXml(string path)
    {
        XmlSerializer serializer = new XmlSerializer(typeof(XmlElement));
        var xmlDocument = new XmlDocument();
        XmlElement root = xmlDocument.CreateElement(ProjectPropertyName);
        var propertyGroup = xmlDocument.CreateElement(PropertyGroupName);
        root.AppendChild(propertyGroup);

        SerializeProperties(propertyGroup, xmlDocument.CreateElement);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = System.Text.Encoding.UTF8,
        };

        using var writer = XmlWriter.Create(path, settings);
        serializer.Serialize(writer, root);
    }

    protected abstract void SerializeProperties(XmlElement propertyGroup, Func<string, XmlElement> createElement);

    protected static Dictionary<string, string> DeserializeProperties(string path) => XDocument.Load(path)
        .Descendants(ProjectPropertyName)
        .Descendants(PropertyGroupName)
        .Descendants()
        .ToDictionary(element => element.Name.LocalName, element => element.Value);

    protected static void SerializeDictionary(
        Dictionary<string, string> properties,
        XmlElement propertyGroup,
        Func<string, XmlElement> createElement)
    {
        foreach (var key in properties.Keys.OrderBy(k => k))
        {
            var element = createElement(key);
            element.InnerText = properties[key];
            propertyGroup.AppendChild(element);
        }
    }
}

