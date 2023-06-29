// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using FluentAssertions;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.Models.VirtualMonoRepo;

[TestFixture]
public class MsBuildPropsFileTests
{
    private string? _outputFile;
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
        var runtimeVersion = new VmrDependencyVersion("26a71c61fbda229f151afb14e274604b4926df5c", "7.0.0-rc.1.22403.8");
        var sdkVersion = new VmrDependencyVersion("6e00e543bbeb8e0491420e2f6b3f7d235166596d", "7.0.100-rc.1.22404.18");

        var propsFile = new PropsFile(
            true,
            new Dictionary<string, string>
            {
                { "sdkGitCommitHash", sdkVersion.Sha },
                { "sdkOutputPackageVersion", sdkVersion.PackageVersion! },
                { "runtimeGitCommitHash", runtimeVersion.Sha },
                { "runtimeOutputPackageVersion", runtimeVersion.PackageVersion! },
            });

        propsFile.SerializeToXml(_outputFile ?? throw new Exception("Output file is not initialized"));

        var content = File.ReadAllText(_outputFile);
        content.Trim().Should().Be(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <runtimeGitCommitHash>{runtimeVersion.Sha}</runtimeGitCommitHash>
                <runtimeOutputPackageVersion>{runtimeVersion.PackageVersion}</runtimeOutputPackageVersion>
                <sdkGitCommitHash>{sdkVersion.Sha}</sdkGitCommitHash>
                <sdkOutputPackageVersion>{sdkVersion.PackageVersion}</sdkOutputPackageVersion>
              </PropertyGroup>
            </Project>
            """);

        var properties = PropsFile.Deserialize(_outputFile);
        properties["runtimeGitCommitHash"].Should().Be(runtimeVersion.Sha);
        properties["runtimeOutputPackageVersion"].Should().Be(runtimeVersion.PackageVersion!);
        properties["sdkGitCommitHash"].Should().Be(sdkVersion.Sha);
        properties["sdkOutputPackageVersion"].Should().Be(sdkVersion.PackageVersion!);
    }
}
