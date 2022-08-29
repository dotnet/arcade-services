// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

#nullable enable
namespace Microsoft.DotNet.Darc.Models.VirtualMonoRepo;

public class AllVersionsPropsFile
{
    public Dictionary<string, string> Versions { get; }

    public AllVersionsPropsFile(Dictionary<string, string> versions)
    {
        Versions = versions;
    }

    public void SerializeToXml(string path)
    {
        XmlSerializer serializer = new XmlSerializer(typeof(XmlElement));
        var xmlDocument = new XmlDocument();
        XmlElement root = xmlDocument.CreateElement("Project");
        var propertyGroup = xmlDocument.CreateElement("PropertyGroup");
        root.AppendChild(propertyGroup);

        foreach (var key in Versions.Keys.OrderBy(k => k))
        {
            var element = xmlDocument.CreateElement(key);
            element.InnerText = Versions[key];
            propertyGroup.AppendChild(element);
        }

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = System.Text.Encoding.UTF8,
        };

        using var writer = XmlWriter.Create(path, settings);
        serializer.Serialize(writer, root);
    }

    public static AllVersionsPropsFile DeserializeFromXml(string path)
    {
        var versions = XDocument.Load(path)
            .Descendants("Project")
            .Descendants("PropertyGroup")
            .Descendants()
            .ToDictionary(element => element.Name.LocalName, element => element.Value);

        return new AllVersionsPropsFile(versions);
    }
}
