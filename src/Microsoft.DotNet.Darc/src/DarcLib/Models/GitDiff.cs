// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib
{
    public class GitDiff
    {
        public static GitDiff NoDiff(string version)
        {
            return new GitDiff()
            {
                BaseVersion = version,
                TargetVersion = version,
                Ahead = 0,
                Behind = 0,
                Valid = true,
            };
        }

        public static GitDiff UnknownDiff()
        {
            return new GitDiff()
            {
                Valid = false,
            };
        }

        public string BaseVersion { get; set; }
        public string TargetVersion { get; set; }
        public int Ahead { get; set; }
        public int Behind { get; set; }
        public bool Valid { get; set; }
    }
}
