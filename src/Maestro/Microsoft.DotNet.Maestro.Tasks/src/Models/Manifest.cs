// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;

namespace Microsoft.DotNet.Maestro.Tasks
{
    [XmlRoot(ElementName = "Build")]
    public class Manifest
    {
        [XmlElement(ElementName = "Package")]
        public List<Package> Packages { get; set; }

        [XmlElement(ElementName = "Blob")]
        public List<Blob> Blobs { get; set; }

        [XmlElement(ElementName = "SigningInformation")]
        public SigningInformation SigningInformation { get; set; }

        [XmlAttribute(AttributeName = "Name")]
        public string Name { get; set; }

        [XmlAttribute(AttributeName = "BuildId")]
        public string BuildId { get; set; }

        [XmlAttribute(AttributeName = "Branch")]
        public string Branch { get; set; }

        [XmlAttribute(AttributeName = "Commit")]
        public string Commit { get; set; }

        [XmlAttribute(AttributeName = "Location")]
        public string Location { get; set; }

        [XmlAttribute(AttributeName = "PublishingVersion")]
        public int PublishingVersion { get; set; }
        
        [XmlAttribute(AttributeName = "IsReleaseOnlyPackageVersion")]
        public string IsReleaseOnlyPackageVersion { get; set; } = "false";

        #region Properties to be used in new publishing flow

        [XmlAttribute(AttributeName = "InitialAssetsLocation")]
        public string InitialAssetsLocation { get; set; }

        // XmlSerializer can't handle nullable fields
        [XmlIgnore]
        public int? AzureDevOpsBuildId { get; set; }

        [XmlAttribute(AttributeName = "AzureDevOpsBuildId")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string AzureDevOpsBuildIdString
        {
            get => AzureDevOpsBuildId?.ToString();
            set => AzureDevOpsBuildId = !string.IsNullOrEmpty(value) ? int.Parse(value) : default(int?);
        }

        [XmlIgnore]
        public int? AzureDevOpsBuildDefinitionId { get; set; }

        [XmlAttribute(AttributeName = "AzureDevOpsBuildDefinitionId")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string AzureDevOpsBuildDefinitionIdString
        {
            get => AzureDevOpsBuildDefinitionId?.ToString();
            set => AzureDevOpsBuildDefinitionId = !string.IsNullOrEmpty(value) ? int.Parse(value) : default(int?);
        }

        [XmlAttribute(AttributeName = "IsStable")]
        public string IsStable { get; set; } = "false";

        [XmlAttribute(AttributeName = "AzureDevOpsAccount")]
        public string AzureDevOpsAccount { get; set; }

        [XmlAttribute(AttributeName = "AzureDevOpsProject")]
        public string AzureDevOpsProject { get; set; }

        [XmlAttribute(AttributeName = "AzureDevOpsBuildNumber")]
        public string AzureDevOpsBuildNumber { get; set; }

        [XmlAttribute(AttributeName = "AzureDevOpsRepository")]
        public string AzureDevOpsRepository { get; set; }

        [XmlAttribute(AttributeName = "AzureDevOpsBranch")]
        public string AzureDevOpsBranch { get; set; }

        #endregion
    }

    [XmlRoot(ElementName = "Package")]
    public class Package
    {
        [XmlAttribute(AttributeName = "Id")]
        public string Id { get; set; }

        [XmlAttribute(AttributeName = "Version")]
        public string Version { get; set; }

        [XmlAttribute(AttributeName = "NonShipping")]
        public bool NonShipping { get; set; }
    }

    [XmlRoot(ElementName = "Blob")]
    public class Blob
    {
        [XmlAttribute(AttributeName = "Id")]
        public string Id { get; set; }

        [XmlAttribute(AttributeName = "NonShipping")]
        public bool NonShipping { get; set; }
    }

    [XmlRoot(ElementName = "SigningInformation")]
    public class SigningInformation
    {
        [XmlAttribute(AttributeName = "AzureDevOpsCollectionUri")]
        public string AzureDevOpsCollectionUri { get; set; }

        [XmlAttribute(AttributeName = "AzureDevOpsProject")]
        public string AzureDevOpsProject { get; set; }

        [XmlAttribute(AttributeName = "AzureDevOpsBuildId")]
        public string AzureDevOpsBuildId { get; set; }

        [XmlElement(ElementName = "FileExtensionSignInfo")]
        public List<FileExtensionSignInfo> FileExtensionSignInfos { get; set; }

        [XmlElement(ElementName = "FileSignInfo")]
        public List<FileSignInfo> FileSignInfos { get; set; }

        [XmlElement(ElementName = "CertificatesSignInfo")]
        public List<CertificatesSignInfo> CertificatesSignInfo { get; set; }

        [XmlElement(ElementName = "ItemsToSign")]
        public List<ItemsToSign> ItemsToSign { get; set; }

        [XmlElement(ElementName = "StrongNameSignInfo")]
        public List<StrongNameSignInfo> StrongNameSignInfos { get; set; }
    }

    [XmlRoot(ElementName = "FileExtensionSignInfo")]
    public class FileExtensionSignInfo
    {
        [XmlAttribute(AttributeName = "Include")]
        public string Include { get; set; }

        [XmlAttribute(AttributeName = "CertificateName")]
        public string CertificateName { get; set; }
    }

    [XmlRoot(ElementName = "FileSignInfo")]
    public class FileSignInfo
    {
        [XmlAttribute(AttributeName = "Include")]
        public string Include { get; set; }

        [XmlAttribute(AttributeName = "CertificateName")]
        public string CertificateName { get; set; }
    }

    [XmlRoot(ElementName = "CertificatesSignInfo")]
    public class CertificatesSignInfo
    {
        [XmlAttribute(AttributeName = "Include")]
        public string Include { get; set; }

        [XmlAttribute(AttributeName = "DualSigningAllowed")]
        public bool DualSigningAllowed { get; set; }
    }

    [XmlRoot(ElementName = "ItemsToSign")]
    public class ItemsToSign
    {
        [XmlAttribute(AttributeName = "Include")]
        public string Include { get; set; }
    }

    [XmlRoot(ElementName = "StrongNameSignInfo")]
    public class StrongNameSignInfo
    {
        [XmlAttribute(AttributeName = "Include")]
        public string Include { get; set; }

        [XmlAttribute(AttributeName = "PublicKeyToken")]
        public string PublicKeyToken { get; set; }

        [XmlAttribute(AttributeName = "CertificateName")]
        public string CertificateName { get; set; }
    }
}
