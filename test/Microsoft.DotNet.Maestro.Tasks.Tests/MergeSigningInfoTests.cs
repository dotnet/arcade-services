// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests;

[TestFixture]
public class MergeSigningInfoTests
{
    public const string Commit = "e7a79ce64f0703c231e6da88b5279dd0bf681b3d";
    public const string AzureDevOpsAccount1 = "dnceng";
    public const int AzureDevOpsBuildDefinitionId1 = 6;
    public const int AzureDevOpsBuildId1 = 856354;
    public const string AzureDevOpsBranch1 = "refs/heads/main";
    public const string AzureDevOpsBuildNumber1 = "20201016.5";
    public const string AzureDevOpsProject1 = "internal";
    public const string AzureDevOpsRepository1 = "https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-arcade";
    public const string LocationString = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts";

    public static readonly List<SigningInformation> ExpectedSigningInfo =
    [
        new SigningInformation()
        {
            CertificatesSignInfo =
            [
                new CertificatesSignInfo()
                {
                    DualSigningAllowed = true,
                    Include = "ThisIsACert"
                }
            ],

            FileExtensionSignInfos =
            [
                new FileExtensionSignInfo()
                {
                    CertificateName = "ThisIsACert",
                    Include = ".dll"
                }
            ],

            FileSignInfos =
            [
                new FileSignInfo()
                {
                    CertificateName = "ThisIsACert",
                    Include = "ALibrary.dll"
                }
            ],

            StrongNameSignInfos =
            [
                new StrongNameSignInfo()
                {
                    CertificateName = "ThisIsACert",
                    Include = "IncludeMe",
                    PublicKeyToken = "123456789abcde12"
                }
            ],
            ItemsToSign = []
        }
    ];

    public static readonly SigningInformation PartialSigningInfo1 = new();

    public static readonly SigningInformation PartialSigningInfo2 = new()
    {
        CertificatesSignInfo =
        [
            new CertificatesSignInfo()
            {
                DualSigningAllowed = true,
                Include = "ThisIsACert"
            }
        ],

        FileExtensionSignInfos = [],
        FileSignInfos = [],
        StrongNameSignInfos = [],
        ItemsToSign = []
    };

    public static readonly SigningInformation PartialSigningInfo4 = new()
    {
        CertificatesSignInfo = [],
        FileExtensionSignInfos =
        [
            new FileExtensionSignInfo()
            {
                CertificateName = "ThisIsACert",
                Include = ".dll"
            }
        ],
        FileSignInfos = [],
        StrongNameSignInfos = [],
        ItemsToSign = []
    };

    public static readonly SigningInformation MergedPartialMetadataSigningInfos = new()
    {
        CertificatesSignInfo =
        [
            new CertificatesSignInfo()
            {
                DualSigningAllowed = true,
                Include = "ThisIsACert"
            }
        ],
        FileExtensionSignInfos =
        [
            new FileExtensionSignInfo()
            {
                CertificateName = "ThisIsACert",
                Include = ".dll"
            }
        ],
        FileSignInfos = [],
        ItemsToSign = [],
        StrongNameSignInfos = []
    };

    public static readonly SigningInformation MergedPartialSigningInfos = new()
    {
        CertificatesSignInfo =
        [
            new CertificatesSignInfo()
            {
                DualSigningAllowed = true,
                Include = "ThisIsACert"
            }
        ],
        FileExtensionSignInfos =
        [
            new FileExtensionSignInfo()
            {
                CertificateName = "ThisIsACert",
                Include = ".dll"
            }
        ],
        FileSignInfos = [],
        StrongNameSignInfos = [],
        ItemsToSign = []
    };

    public static readonly List<SigningInformation> ExpectedSigningInfo2 =
    [
        new SigningInformation()
        {
            CertificatesSignInfo =
            [
                new CertificatesSignInfo()
                {
                    DualSigningAllowed = true,
                    Include = "AnotherCert"
                }
            ],

            FileExtensionSignInfos =
            [
                new FileExtensionSignInfo()
                {
                    CertificateName = "None",
                    Include = ".zip"
                }
            ],

            FileSignInfos =
            [
                new FileSignInfo()
                {
                    CertificateName = "AnotherCert",
                    Include = "AnotherLibrary.dll"
                }
            ],

            StrongNameSignInfos =
            [
                new StrongNameSignInfo()
                {
                    CertificateName = "AnotherCert",
                    Include = "StrongName",
                    PublicKeyToken = "123456789abcde12"
                }
            ],
            ItemsToSign = []
        }
    ];

    public static readonly SigningInformation ExpectedMergedSigningInfo =
        new()
        {
            CertificatesSignInfo =
            [
                new CertificatesSignInfo()
                {
                    DualSigningAllowed = true,
                    Include = "ThisIsACert"
                },
                new CertificatesSignInfo()
                {
                    DualSigningAllowed = true,
                    Include = "AnotherCert"
                }
            ],

            FileExtensionSignInfos =
            [
                new FileExtensionSignInfo()
                {
                    CertificateName = "ThisIsACert",
                    Include = ".dll"
                },
                new FileExtensionSignInfo()
                {
                    CertificateName = "None",
                    Include = ".zip"
                }
            ],

            FileSignInfos =
            [
                new FileSignInfo()
                {
                    CertificateName = "ThisIsACert",
                    Include = "ALibrary.dll"
                },
                new FileSignInfo()
                {
                    CertificateName = "AnotherCert",
                    Include = "AnotherLibrary.dll"
                }
            ],

            StrongNameSignInfos =
            [
                new StrongNameSignInfo()
                {
                    CertificateName = "ThisIsACert",
                    Include = "IncludeMe",
                    PublicKeyToken = "123456789abcde12"
                },
                new StrongNameSignInfo()
                {
                    CertificateName = "AnotherCert",
                    Include = "StrongName",
                    PublicKeyToken = "123456789abcde12"
                }
            ],
            ItemsToSign = []
        };

    [Test]
    public void GivenCompatibleSigningInfo()
    {
        SigningInformation actualMerged = PushMetadataToBuildAssetRegistry.MergeSigningInfo([.. ExpectedSigningInfo, .. ExpectedSigningInfo2]);
        actualMerged.Should().BeEquivalentTo(ExpectedMergedSigningInfo);
    }

    [Test]
    public void GivenDuplicateSigningInfo()
    {
        SigningInformation actualMerged = PushMetadataToBuildAssetRegistry.MergeSigningInfo([.. ExpectedSigningInfo, .. ExpectedSigningInfo]);
        actualMerged.Should().BeEquivalentTo(ExpectedSigningInfo.First());
    }

    [Test]
    public void GivenTwoPartialSigningInfosWithEmptySections()
    {
        SigningInformation actualMerged = PushMetadataToBuildAssetRegistry.MergeSigningInfo([PartialSigningInfo2, PartialSigningInfo4]);
        actualMerged.Should().BeEquivalentTo(MergedPartialMetadataSigningInfos);
    }

    [Test]
    public void GivenNullSigningInfoList()
    {
        Action act = () => PushMetadataToBuildAssetRegistry.MergeSigningInfo(null);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void GivenEmptySigningInfoList()
    {
        SigningInformation actualMerged = PushMetadataToBuildAssetRegistry.MergeSigningInfo([]);
        actualMerged.Should().Be(null);
    }
}
