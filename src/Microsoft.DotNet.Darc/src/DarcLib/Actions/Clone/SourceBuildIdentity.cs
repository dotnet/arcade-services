// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class SourceBuildIdentity
    {
        public static IEqualityComparer<SourceBuildIdentity> CaseInsensitiveComparer { get; } =
            new CaseInsensitiveComparerImplementation();

        public static IEqualityComparer<SourceBuildIdentity> RepoNameOnlyComparer { get; } =
            new RepoNameOnlyComparerImplementation();

        public string RepoUri { get; }
        public string Commit { get; }

        /// <summary>
        /// The source of this identity, or null if this didn't come from a DarcLib dependency.
        /// </summary>
        public DependencyDetail Source { get; }

        public SourceBuildIdentity(
            string repoUri,
            string commit,
            DependencyDetail source)
        {
            RepoUri = repoUri;
            Commit = commit;
            Source = source;
        }

        public override string ToString() => $"{RepoUri}@{ShortCommit}";

        public string ShortCommit => string.IsNullOrEmpty(Commit) ? "" : Commit.Substring(0, 8);

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

                return string.Equals(x.RepoUri, y.RepoUri, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Commit, y.Commit, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(SourceBuildIdentity obj) =>
                (obj.RepoUri, obj.Commit).GetHashCode();
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

                return string.Equals(x.RepoUri, y.RepoUri, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(SourceBuildIdentity obj) => obj.RepoUri.GetHashCode();
        }

        public static SourceBuildIdentity Create(JObject obj)
        {
            return new SourceBuildIdentity(
                obj.Value<string>(nameof(RepoUri)),
                obj.Value<string>(nameof(Commit)),
                obj[nameof(Source)].ToObject<DependencyDetail>());
        }

        public JObject ToJObject()
        {
            return JObject.FromObject(new
            {
                RepoUri,
                Commit,
                Source
            });
        }
    }
}
