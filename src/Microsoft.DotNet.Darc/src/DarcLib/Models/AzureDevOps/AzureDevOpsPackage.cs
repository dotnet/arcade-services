// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib
{
    public class AzureDevOpsPackage
    {

        public string Name { get; set; }

        public string ProtocolType { get; set; }

        public AzureDevOpsPackageVersion[] Versions { get; set; }

        public AzureDevOpsPackage(string name, string protocolType)
        {
            Name = name;
            ProtocolType = protocolType;
        }
    }

    public class AzureDevOpsPackageVersion
    {
        public string Version { get; set; }
        public bool IsDeleted { get; set; }

        public AzureDevOpsPackageVersion(string version, bool isDeleted)
        {
            Version = version;
            IsDeleted = isDeleted;
        }
    }
}
