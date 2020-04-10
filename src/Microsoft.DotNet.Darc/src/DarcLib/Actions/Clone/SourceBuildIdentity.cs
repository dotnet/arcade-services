// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    /// <summary>
    /// The identity of a repo that must be evaluated to form a darc clone graph. This comes from a
    /// dependency, so there is limited information.
    /// </summary>
    public class SourceBuildIdentity
    {
        public static IEqualityComparer<SourceBuildIdentity> CaseInsensitiveComparer { get; } =
            new CaseInsensitiveComparerImplementation();

        public static IEqualityComparer<SourceBuildIdentity> RepoNameOnlyComparer { get; } =
            new RepoNameOnlyComparerImplementation();

        public string RepoUri { get; set; }
        public string Commit { get; set; }

        public override string ToString() => $"{RepoUri}@{ShortCommit}";

        public string ShortCommit => string.IsNullOrEmpty(Commit)
            ? ""
            : Commit.Substring(0, Math.Min(Commit.Length, 8));

        private class CaseInsensitiveComparerImplementation : IEqualityComparer<SourceBuildIdentity>
        {
            public bool Equals(SourceBuildIdentity x, SourceBuildIdentity y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return string.Equals(x.RepoUri, y.RepoUri, StringComparison.InvariantCultureIgnoreCase) &&
                    string.Equals(x.Commit, y.Commit, StringComparison.InvariantCultureIgnoreCase);
            }

            public int GetHashCode(SourceBuildIdentity obj) => (
                obj.RepoUri.ToLowerInvariant(),
                obj.Commit?.ToLowerInvariant())
                .GetHashCode();
        }

        private class RepoNameOnlyComparerImplementation : IEqualityComparer<SourceBuildIdentity>
        {
            public bool Equals(SourceBuildIdentity x, SourceBuildIdentity y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return string.Equals(x.RepoUri, y.RepoUri, StringComparison.InvariantCultureIgnoreCase);
            }

            public int GetHashCode(SourceBuildIdentity obj) =>
                obj.RepoUri.ToLowerInvariant().GetHashCode();
        }
    }
}
