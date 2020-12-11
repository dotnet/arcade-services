// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Microsoft.DotNet.Maestro.Tasks.Tests
{
    [TestFixture]
    public class MergeSigningInfoTests
    {
        private PushMetadataToBuildAssetRegistry pushMetadata;

        public const string Commit = "e7a79ce64f0703c231e6da88b5279dd0bf681b3d";
        public const string AzureDevOpsAccount1 = "dnceng";
        public const int AzureDevOpsBuildDefinitionId1 = 6;
        public const int AzureDevOpsBuildId1 = 856354;
        public const string AzureDevOpsBranch1 = "refs/heads/master";
        public const string AzureDevOpsBuildNumber1 = "20201016.5";
        public const string AzureDevOpsProject1 = "internal";
        public const string AzureDevOpsRepository1 = "https://dnceng@dev.azure.com/dnceng/internal/_git/dotnet-arcade";
        public const string LocationString = "https://dev.azure.com/dnceng/internal/_apis/build/builds/856354/artifacts";

        public static readonly List<SigningInformation> ExpectedSigningInfo = new List<SigningInformation>()
            {
                new SigningInformation()
                {
                    CertificatesSignInfo = new List<CertificatesSignInfo>()
                    {
                        new CertificatesSignInfo()
                        {
                            DualSigningAllowed = true,
                            Include = "ThisIsACert"
                        }
                    },

                    FileExtensionSignInfos = new List<FileExtensionSignInfo>()
                    {
                        new FileExtensionSignInfo()
                        {
                            CertificateName = "ThisIsACert",
                            Include = ".dll"
                        }
                    },

                    FileSignInfos = new List<FileSignInfo>()
                    {
                        new FileSignInfo()
                        {
                            CertificateName = "ThisIsACert",
                             Include = "ALibrary.dll"
                        }
                    },

                    StrongNameSignInfos = new List<StrongNameSignInfo>()
                    {
                        new StrongNameSignInfo()
                        {
                            CertificateName = "ThisIsACert",
                            Include = "IncludeMe",
                            PublicKeyToken = "123456789abcde12"
                        }
                    },
                    ItemsToSign = new List<ItemsToSign>()
                }
            };

        public static readonly SigningInformation PartialSigningInfo1 = new SigningInformation();

        public static readonly SigningInformation PartialSigningInfo2 = new SigningInformation()
        {
            CertificatesSignInfo = new List<CertificatesSignInfo>()
                {
                    new CertificatesSignInfo()
                    {
                        DualSigningAllowed = true,
                        Include = "ThisIsACert"
                    }
                },

            FileExtensionSignInfos = new List<FileExtensionSignInfo>(),
            FileSignInfos = new List<FileSignInfo>(),
            StrongNameSignInfos = new List<StrongNameSignInfo>(),
            ItemsToSign = new List<ItemsToSign>()
        };

        public static readonly SigningInformation PartialSigningInfo4 = new SigningInformation()
        {
            CertificatesSignInfo = new List<CertificatesSignInfo>(),
            FileExtensionSignInfos = new List<FileExtensionSignInfo>()
                {
                    new FileExtensionSignInfo()
                    {
                        CertificateName = "ThisIsACert",
                        Include = ".dll"
                    }
                },
            FileSignInfos = new List<FileSignInfo>(),
            StrongNameSignInfos = new List<StrongNameSignInfo>(),
            ItemsToSign = new List<ItemsToSign>()
        };

        public static readonly SigningInformation MergedPartialMetadataSigningInfos = new SigningInformation()
        {
            CertificatesSignInfo = new List<CertificatesSignInfo>()
                {
                    new CertificatesSignInfo()
                    {
                        DualSigningAllowed = true,
                        Include = "ThisIsACert"
                    }
                },
            FileExtensionSignInfos = new List<FileExtensionSignInfo>()
                {
                    new FileExtensionSignInfo()
                    {
                        CertificateName = "ThisIsACert",
                        Include = ".dll"
                    }
                },
            FileSignInfos = new List<FileSignInfo>(),
            ItemsToSign = new List<ItemsToSign>(),
            StrongNameSignInfos = new List<StrongNameSignInfo>()
        };

        public static readonly SigningInformation MergedPartialSigningInfos = new SigningInformation()
        {
            CertificatesSignInfo = new List<CertificatesSignInfo>()
                {
                    new CertificatesSignInfo()
                    {
                        DualSigningAllowed = true,
                        Include = "ThisIsACert"
                    }
                },
            FileExtensionSignInfos = new List<FileExtensionSignInfo>()
                {
                    new FileExtensionSignInfo()
                    {
                        CertificateName = "ThisIsACert",
                        Include = ".dll"
                    }
                },
            FileSignInfos = new List<FileSignInfo>(),
            StrongNameSignInfos = new List<StrongNameSignInfo>(),
            ItemsToSign = new List<ItemsToSign>()
        };

        public static readonly List<SigningInformation> ExpectedSigningInfo2 = new List<SigningInformation>()
            {
                new SigningInformation()
                {
                    CertificatesSignInfo = new List<CertificatesSignInfo>()
                    {
                        new CertificatesSignInfo()
                        {
                            DualSigningAllowed = true,
                            Include = "AnotherCert"
                        }
                    },

                    FileExtensionSignInfos = new List<FileExtensionSignInfo>()
                    {
                        new FileExtensionSignInfo()
                        {
                            CertificateName = "None",
                            Include = ".zip"
                        }
                    },

                    FileSignInfos = new List<FileSignInfo>()
                    {
                        new FileSignInfo()
                        {
                            CertificateName = "AnotherCert",
                             Include = "AnotherLibrary.dll"
                        }
                    },

                    StrongNameSignInfos = new List<StrongNameSignInfo>()
                    {
                        new StrongNameSignInfo()
                        {
                            CertificateName = "AnotherCert",
                            Include = "StrongName",
                            PublicKeyToken = "123456789abcde12"
                        }
                    },
                    ItemsToSign = new List<ItemsToSign>()
                }
            };

        public static readonly SigningInformation ExpectedMergedSigningInfo =
                new SigningInformation()
                {
                    CertificatesSignInfo = new List<CertificatesSignInfo>()
                    {
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
                    },

                    FileExtensionSignInfos = new List<FileExtensionSignInfo>()
                    {
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
                    },

                    FileSignInfos = new List<FileSignInfo>()
                    {
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
                    },

                    StrongNameSignInfos = new List<StrongNameSignInfo>()
                    {
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
                    },
                    ItemsToSign = new List<ItemsToSign>()
                };

        [SetUp]
        public void SetupMergeSigningInfo()
        {
            pushMetadata = new PushMetadataToBuildAssetRegistry();
        }

        [Test]
        public void GivenCompatibleSigningInfo()
        {
            SigningInformation actualMerged = pushMetadata.MergeSigningInfo(ExpectedSigningInfo.Concat(ExpectedSigningInfo2).ToList());
            actualMerged.Should().BeEquivalentTo(ExpectedMergedSigningInfo);
        }

        [Test]
        public void GivenDuplicateSigningInfo()
        {
            SigningInformation actualMerged = pushMetadata.MergeSigningInfo(ExpectedSigningInfo.Concat(ExpectedSigningInfo).ToList());
            actualMerged.Should().BeEquivalentTo(ExpectedSigningInfo.First());
        }

        [Test]
        public void GivenTwoPartialSigningInfosWithEmptySections()
        {
            SigningInformation actualMerged = pushMetadata.MergeSigningInfo(new List<SigningInformation> { PartialSigningInfo2, PartialSigningInfo4 });
            actualMerged.Should().BeEquivalentTo(MergedPartialMetadataSigningInfos);
        }

        [Test]
        public void GivenNullSigningInfoList()
        {
            Action act = () => pushMetadata.MergeSigningInfo(null);
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void GivenEmptySigningInfoList()
        {
            SigningInformation actualMerged = pushMetadata.MergeSigningInfo(new List<SigningInformation>());
            actualMerged.Should().Be(null);
        }
    }
}
