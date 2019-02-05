// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.DarcLib
{
    public class AzureDevOpsReleaseDefinition
    {
        public long Id { get; set; }

        public string Name { get; set; }

        public AzureDevOpsArtifact[] Artifacts { get; set; }

        public object[] Environments { get; set; }

        public long Revision { get; set; }

        public string Source { get; set; }

        public string Description { get; set; }

        public object CreatedBy { get; set; }

        public DateTimeOffset CreatedOn { get; set; }

        public object ModifiedBy { get; set; }

        public DateTimeOffset ModifiedOn { get; set; }

        public bool IsDeleted { get; set; }

        public object Variables { get; set; }

        public long[] VariableGroups { get; set; }

        public object[] Triggers { get; set; }

        public string ReleaseNameFormat { get; set; }

        public string[] Tags { get; set; }

        public object Properties { get; set; }

        public string Path { get; set; }

        public object ProjectReference { get; set; }
    }
}
