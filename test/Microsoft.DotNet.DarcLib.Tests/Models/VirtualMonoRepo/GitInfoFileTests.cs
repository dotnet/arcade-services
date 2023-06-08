// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using FluentAssertions;
using Microsoft.DotNet.Darc.Models.VirtualMonoRepo;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.Models.VirtualMonoRepo;

[TestFixture]
public class GitInfoFileTests
{
    private string? _outputFile;

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
    public void GitInfoXmlIsDeSerializedTest()
    {
        var gitInfoFile = new GitInfoFile
        {
            GitCommitHash = "4ee620cc1b57da45d93135e064d43a83e65bbb6e",
            OfficialBuildId = "20220803.1",
            OutputPackageVersion = "7.0.0-beta.22403.1",
            PreReleaseVersionLabel = "beta",
            GitCommitCount = 1432,
            IsStable = true,
        };

        // Act
        gitInfoFile.SerializeToXml(_outputFile ?? throw new Exception("Output file is not initialized"));

        // Verify
        var content = File.ReadAllText(_outputFile);        
        content.Trim().Should().Be(
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Project>
              <PropertyGroup>
                <GitCommitHash>4ee620cc1b57da45d93135e064d43a83e65bbb6e</GitCommitHash>
                <OfficialBuildId>20220803.1</OfficialBuildId>
                <OutputPackageVersion>7.0.0-beta.22403.1</OutputPackageVersion>
                <PreReleaseVersionLabel>beta</PreReleaseVersionLabel>
                <IsStable>true</IsStable>
                <GitCommitCount>1432</GitCommitCount>
              </PropertyGroup>
            </Project>
            """);

        gitInfoFile = GitInfoFile.DeserializeFromXml(_outputFile);
        
        gitInfoFile.GitCommitHash.Should().Be("4ee620cc1b57da45d93135e064d43a83e65bbb6e");
        gitInfoFile.OfficialBuildId.Should().Be("20220803.1");
        gitInfoFile.OutputPackageVersion.Should().Be("7.0.0-beta.22403.1");
        gitInfoFile.PreReleaseVersionLabel.Should().Be("beta");
        gitInfoFile.GitCommitCount.Should().Be(1432);
        gitInfoFile.IsStable.Should().BeTrue();
    }
}
