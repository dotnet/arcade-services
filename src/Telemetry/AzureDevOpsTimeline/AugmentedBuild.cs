// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public class AugmentedBuild
    {
        public AugmentedBuild(Build build, string targetBranch)
        {
            Build = build;
            TargetBranch = targetBranch;
        }

        public Build Build { get; }
        public string TargetBranch { get; }
    }
}
