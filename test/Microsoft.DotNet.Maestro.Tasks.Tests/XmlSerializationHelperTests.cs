// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

[TestFixture]
public class XmlSerializationHelperTests
{
    private const string Certificate1Name = "ImaginaryCert1";
    private const string Certificate2Name = "ImaginaryCert2";
    private const string PublicKeyToken1 = "123456789101112a";
    private const string PublicKeyToken2 = "48151623421051aa";
    private const string TargetFramework1 = "Bullseye.Net, Version = v1.0";
    private const string TargetFramework2 = "Bullseye.Net, Version = v1.1";

    [Test]
    public void FileSignInfoTest()
    {
        var signingInformation = GetTestInfo();
        XElement serializationResult = XmlSerializationHelper.SigningInfoToXml(signingInformation);

        var fileSignInfos = serializationResult.Descendants("FileSignInfo").ToList();
        fileSignInfos.Count().Should().Be(3);

        // Validate that the PKT and TFM are only included if supplied
        var someAssemblySignInfos = fileSignInfos.Where(x => x.Attribute("Include").Value.Equals("SomeAssembly.dll"));
        someAssemblySignInfos.Count().Should().Be(2);
        // In real scenarios, the assembly will be expected to share PKT across certificate / TFM
        someAssemblySignInfos.Where(s => s.Attribute("PublicKeyToken").Value == PublicKeyToken1).Count().Should().Be(2);
        // Make sure different cert & TFMs made it through:
        someAssemblySignInfos.Where(s => s.Attribute("CertificateName").Value == Certificate1Name).Count().Should().Be(1);
        someAssemblySignInfos.Where(s => s.Attribute("TargetFramework").Value == TargetFramework1).Count().Should().Be(1);
        someAssemblySignInfos.Where(s => s.Attribute("CertificateName").Value == Certificate2Name).Count().Should().Be(1);
        someAssemblySignInfos.Where(s => s.Attribute("TargetFramework").Value == TargetFramework2).Count().Should().Be(1);

        // Eventually we need tests where this is actually checked to make sure every entry for a given assembly has
        // both a PKT / TFM or not, to avoid ambiguity.
        var someOtherAssemblySignInfo = fileSignInfos.Where(x => x.Attribute("Include").Value.Equals("SomeOtherAssembly.dll")).Single();
        someOtherAssemblySignInfo.Attribute("PublicKeyToken").Should().BeNull();
        someOtherAssemblySignInfo.Attribute("TargetFramework").Should().BeNull();
    }

    [Test]
    public void FileExtensionSignInfoTest()
    {
        var signingInformation = GetTestInfo();
        XElement serializationResult = XmlSerializationHelper.SigningInfoToXml(signingInformation);

        var fileExtensionSignInfos = serializationResult.Descendants("FileExtensionSignInfo").ToList();
        fileExtensionSignInfos.Count().Should().Be(2);

        var fileExtensionSignInfo1 = fileExtensionSignInfos.Where(x => x.Attribute("Include").Value.Equals(".dll")).Single();
        fileExtensionSignInfo1.Attribute("CertificateName").Value.Should().Be(Certificate1Name);

        var fileExtensionSignInfo2 = fileExtensionSignInfos.Where(x => x.Attribute("Include").Value.Equals(".xbap")).Single();
        fileExtensionSignInfo2.Attribute("CertificateName").Value.Should().Be(Certificate2Name);
    }

    [Test]
    public void CertificatesSignInfoTest()
    {
        var signingInformation = GetTestInfo();
        XElement serializationResult = XmlSerializationHelper.SigningInfoToXml(signingInformation);

        var certificatesSignInfos = serializationResult.Descendants("CertificatesSignInfo").ToList();
        certificatesSignInfos.Count().Should().Be(2);

        var certificatesSignInfo1 = certificatesSignInfos.Where(x => x.Attribute("Include").Value.Equals(Certificate1Name)).Single();
        certificatesSignInfo1.Attribute("DualSigningAllowed").Value.Should().Be("true");

        var certificatesSignInfo2 = certificatesSignInfos.Where(x => x.Attribute("Include").Value.Equals(Certificate2Name)).Single();
        certificatesSignInfo2.Attribute("DualSigningAllowed").Value.Should().Be("false");
    }

    [Test]
    public void StrongNameSignInfoTest()
    {
        var signingInformation = GetTestInfo();
        XElement serializationResult = XmlSerializationHelper.SigningInfoToXml(signingInformation);

        var strongNameSignInfos = serializationResult.Descendants("StrongNameSignInfo").ToList();
        strongNameSignInfos.Count().Should().Be(2);

        var strongNameSignInfo1 = strongNameSignInfos.Where(x => x.Attribute("Include").Value.Equals("StrongName1")).Single();
        strongNameSignInfo1.Attribute("PublicKeyToken").Value.Should().Be(PublicKeyToken1);
        strongNameSignInfo1.Attribute("CertificateName").Value.Should().Be(Certificate1Name);

        var strongNameSignInfo2 = strongNameSignInfos.Where(x => x.Attribute("Include").Value.Equals("StrongName2")).Single();
        strongNameSignInfo2.Attribute("PublicKeyToken").Value.Should().Be(PublicKeyToken2);
        strongNameSignInfo2.Attribute("CertificateName").Value.Should().Be(Certificate2Name);
    }

    [Test]
    public void ItemsToSignTest()
    {
        var signingInformation = GetTestInfo();
        XElement serializationResult = XmlSerializationHelper.SigningInfoToXml(signingInformation);

        var items = serializationResult.Descendants("ItemsToSign").ToList();
        items.Count().Should().Be(2);

        items.Where(i => i.Attribute("Include").Value.Equals("SomeAssembly.dll")).Count().Should().Be(1);
        items.Where(i => i.Attribute("Include").Value.Equals("SomeOtherAssembly.dll")).Count().Should().Be(1);
    }

    [Test]
    public void CalledWithNullSigningInfo()
    {
        XElement serializationResult = XmlSerializationHelper.SigningInfoToXml(null);
        serializationResult.Should().Be(null);
    }

    [Test]
    public void CorrectRootElementType()
    {
        var signingInformation = GetTestInfo();
        XElement serializationResult = XmlSerializationHelper.SigningInfoToXml(signingInformation);
        serializationResult.Name.LocalName.Should().Be("SigningInformation");
    }

    private SigningInformation GetTestInfo()
    {
        SigningInformation signingInfo = new SigningInformation()
        {
            CertificatesSignInfo = new List<CertificatesSignInfo>()
            {
                new CertificatesSignInfo()
                {
                    DualSigningAllowed = true,
                    Include = Certificate1Name,
                },
                new CertificatesSignInfo()
                {
                    DualSigningAllowed = false,
                    Include = Certificate2Name,
                },
            },
            FileExtensionSignInfos = new List<FileExtensionSignInfo>()
            {
                new FileExtensionSignInfo()
                {
                    CertificateName = Certificate1Name,
                    Include = ".dll",
                },
                new FileExtensionSignInfo()
                {
                    CertificateName = Certificate2Name,
                    Include = ".xbap",
                }
            },
            FileSignInfos = new List<FileSignInfo>()
            {
                new FileSignInfo()
                {
                    CertificateName = Certificate1Name,
                    Include = "SomeAssembly.dll",
                    PublicKeyToken = PublicKeyToken1,
                    TargetFramework = TargetFramework1
                },
                new FileSignInfo()
                {
                    CertificateName = Certificate2Name,
                    Include = "SomeAssembly.dll",
                    PublicKeyToken = PublicKeyToken1,
                    TargetFramework = TargetFramework2
                },
                new FileSignInfo()
                {
                    CertificateName = Certificate2Name,
                    Include = "SomeOtherAssembly.dll",
                },
            },
            ItemsToSign = new List<ItemsToSign>()
            {
                new ItemsToSign() { Include = "SomeAssembly.dll" },
                new ItemsToSign() { Include = "SomeOtherAssembly.dll" },
            },
            StrongNameSignInfos = new List<StrongNameSignInfo>()
            {
                new StrongNameSignInfo()
                {
                    CertificateName = Certificate1Name,
                    Include = "StrongName1",
                    PublicKeyToken = PublicKeyToken1
                },
                new StrongNameSignInfo()
                {
                    CertificateName = Certificate2Name,
                    Include = "StrongName2",
                    PublicKeyToken = PublicKeyToken2
                }
            }
        };
        return signingInfo;
    }
}
