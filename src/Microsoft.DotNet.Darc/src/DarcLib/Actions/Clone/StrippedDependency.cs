// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class StrippedDependency
    {
        public string RepoUri { get; set; }
        public string Commit { get; set; }

        public HashSet<StrippedDependency> Dependencies { get; set; }

        public static StrippedDependency GetOrAddDependency(
            List<StrippedDependency> allDependencies,
            DependencyDetail d)
        {
            return GetOrAddDependency(allDependencies, d.RepoUri, d.Commit);
        }

        public static StrippedDependency GetOrAddDependency(
            List<StrippedDependency> allDependencies,
            StrippedDependency d)
        {
            return GetOrAddDependency(allDependencies, d.RepoUri, d.Commit);
        }

        public static StrippedDependency GetOrAddDependency(
            List<StrippedDependency> allDependencies,
            string repoUrl,
            string commit)
        {
            StrippedDependency dep = allDependencies?.SingleOrDefault(d =>
                string.Equals(d.RepoUri, repoUrl, StringComparison.InvariantCultureIgnoreCase) &&
                string.Equals(d.Commit, commit, StringComparison.InvariantCultureIgnoreCase));

            if (dep == null)
            {
                dep = new StrippedDependency(repoUrl, commit);
                allDependencies?.Add(dep);
            }
            return dep;
        }

        private StrippedDependency(string repoUrl, string commit)
        {
            this.RepoUri = repoUrl;
            this.Commit = commit;
            this.Dependencies = new HashSet<StrippedDependency>();
            this.Dependencies.Add(this);
        }

        private StrippedDependency(DependencyDetail d) : this(d.RepoUri, d.Commit) { }

        public void AddDependency(
            List<StrippedDependency> allDependencies,
            StrippedDependency dep)
        {
            StrippedDependency other = GetOrAddDependency(allDependencies, dep);
            if (this.Dependencies.Any(d => d.RepoUri.ToLowerInvariant() == other.RepoUri.ToLowerInvariant()))
            {
                return;
            }
            this.Dependencies.Add(other);
            foreach (StrippedDependency sameUrl in allDependencies.Where(d => d.RepoUri.ToLowerInvariant() == this.RepoUri.ToLowerInvariant()))
            {
                sameUrl.Dependencies.Add(other);
            }
        }

        public void AddDependency(
            List<StrippedDependency> allDependencies,
            DependencyDetail dep)
        {
            this.AddDependency(allDependencies, GetOrAddDependency(allDependencies, dep));
        }

        public bool HasDependencyOn(string repoUrl)
        {
            bool hasDep = false;

            var visited = new HashSet<StrippedDependency>();

            foreach (StrippedDependency dep in this.Dependencies)
            {
                if (visited.Contains(dep))
                {
                    return false;
                }

                if (dep.RepoUri.ToLowerInvariant() == this.RepoUri.ToLowerInvariant())
                {
                    return false;
                }

                visited.Add(dep);

                hasDep = hasDep || dep.RepoUri.ToLowerInvariant() == repoUrl.ToLowerInvariant() || dep.HasDependencyOn(repoUrl);
                if (hasDep)
                {
                    break;
                }
            }

            return hasDep;
        }

        public bool HasDependencyOn(StrippedDependency dep)
        {
            return HasDependencyOn(dep.RepoUri);
        }

        public bool HasDependencyOn(DependencyDetail dep)
        {
            return HasDependencyOn(dep.RepoUri);
        }

        public override bool Equals(object obj)
        {
            StrippedDependency other = obj as StrippedDependency;
            if (other == null)
            {
                return false;
            }
            return this.RepoUri == other.RepoUri && this.Commit == other.Commit;
        }

        public override int GetHashCode()
        {
            return this.RepoUri.GetHashCode() ^ this.Commit.GetHashCode();
        }

        public override string ToString()
        {
            return $"{this.RepoUri}@{this.Commit} ({this.Dependencies.Count} deps)";
        }
    }
}
