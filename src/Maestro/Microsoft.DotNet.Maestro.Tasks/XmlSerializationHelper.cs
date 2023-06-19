// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Xml.Linq;

namespace Microsoft.DotNet.Maestro.Tasks
{
    static class XmlSerializationHelper
    {
        public static XElement SigningInfoToXml(SigningInformation signingInformation)
        {
            if (signingInformation == null)
            {
                return null;
            }

            List<XElement> signingMetadata = new List<XElement>();

            foreach (FileExtensionSignInfo fileExtensionSignInfo in signingInformation.FileExtensionSignInfos)
            {
                signingMetadata.Add(new XElement(
                    nameof(FileExtensionSignInfo),
                    new XAttribute[]
                    {
                        new XAttribute(nameof(fileExtensionSignInfo.Include), fileExtensionSignInfo.Include),
                        new XAttribute(nameof(fileExtensionSignInfo.CertificateName), fileExtensionSignInfo.CertificateName)
                    }));
            }

            foreach (FileSignInfo fileSignInfo in signingInformation.FileSignInfos)
            {
                List<XAttribute> xAttributes = new List<XAttribute>()
                {
                    new XAttribute(nameof(fileSignInfo.Include), fileSignInfo.Include),
                    new XAttribute(nameof(fileSignInfo.CertificateName), fileSignInfo.CertificateName)
                };

                if (!string.IsNullOrEmpty(fileSignInfo.PublicKeyToken))
                {
                    xAttributes.Add(new XAttribute(nameof(fileSignInfo.PublicKeyToken), fileSignInfo.PublicKeyToken));
                }
                if (!string.IsNullOrEmpty(fileSignInfo.TargetFramework))
                {
                    xAttributes.Add(new XAttribute(nameof(fileSignInfo.TargetFramework), fileSignInfo.TargetFramework));
                }
                signingMetadata.Add(new XElement(nameof(FileSignInfo), xAttributes.ToArray()));
            }

            foreach (CertificatesSignInfo certificatesSignInfo in signingInformation.CertificatesSignInfo)
            {
                signingMetadata.Add(new XElement(
                    nameof(CertificatesSignInfo),
                    new XAttribute[]
                    {
                        new XAttribute(nameof(certificatesSignInfo.Include), certificatesSignInfo.Include),
                        new XAttribute(nameof(certificatesSignInfo.DualSigningAllowed), certificatesSignInfo.DualSigningAllowed)
                    }));
            }

            foreach (ItemsToSign itemsToSign in signingInformation.ItemsToSign)
            {
                signingMetadata.Add(new XElement(
                    nameof(ItemsToSign),
                    new XAttribute[]
                    {
                        new XAttribute(nameof(itemsToSign.Include), itemsToSign.Include)
                    }));
            }

            foreach (StrongNameSignInfo strongNameSignInfo in signingInformation.StrongNameSignInfos)
            {
                signingMetadata.Add(new XElement(
                    nameof(StrongNameSignInfo),
                    new XAttribute[]
                    {
                        new XAttribute(nameof(strongNameSignInfo.Include), strongNameSignInfo.Include),
                        new XAttribute(nameof(strongNameSignInfo.PublicKeyToken), strongNameSignInfo.PublicKeyToken),
                        new XAttribute(nameof(strongNameSignInfo.CertificateName), strongNameSignInfo.CertificateName)
                    }));
            }
            return new XElement(nameof(SigningInformation), signingMetadata);
        }
    }
}
