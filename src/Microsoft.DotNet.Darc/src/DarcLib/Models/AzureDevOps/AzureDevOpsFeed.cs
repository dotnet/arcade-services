// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib
{
    public class AzureDevOpsFeed: AzureDevOpsIdNamePair
    {
        public List<AzureDevOpsPackage> Packages { get; set; }

        public AzureDevOpsProject Project { get; set; }

        public string Account { get; set; }

        public AzureDevOpsFeed(string account, string id, string name,  AzureDevOpsProject project = null)
        {
            Account = account;
            Name = name;
            Id = id;
            Project = project;
            Packages = new List<AzureDevOpsPackage>();
        }
    }
}
