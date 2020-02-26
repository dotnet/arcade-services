// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.Services.Utility
{
    public static class GitHelpers
    {
        private const string RefsHeadsPrefix = "refs/heads/";
        private const string SlashRefsHeadsPrefix = "/refs/heads/";

        public static string NormalizeBranchName(string branch)
        {
            if (branch != null) 
            {
                if (branch.StartsWith(RefsHeadsPrefix)) {
                    return branch.Substring(RefsHeadsPrefix.Length);
                }
                else if (branch.StartsWith(SlashRefsHeadsPrefix))
                {
                    return branch.Substring(SlashRefsHeadsPrefix.Length);
                }
            }
            return branch;
        }
    }
}
