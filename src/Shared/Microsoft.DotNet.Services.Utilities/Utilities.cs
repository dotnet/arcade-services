// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.Services.Utilities
{
    public static class Utilities
    {
        const string refsHeadsPrefix = "refs/heads/";
        public static string NormalizeBranchName(string branch)
        {
            if (branch != null && branch.StartsWith(refsHeadsPrefix))
            {
                return branch.Substring(refsHeadsPrefix.Length);
            }
            return branch;
        }
}
}
