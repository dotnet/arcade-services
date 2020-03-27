// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class StrippedDependency
    {
        public string RepoUri { get; set; }
        public string Commit { get; set; }
        private bool Visited { get; set; }

        public HashSet<StrippedDependency> Dependencies { get; set; }
        public static HashSet<StrippedDependency> AllDependencies;

        static StrippedDependency()
        {
            AllDependencies = new HashSet<StrippedDependency>();
        }

        public static StrippedDependency GetDependency(DependencyDetail d)
        {
            return GetDependency(d.RepoUri, d.Commit);
        }

        public static StrippedDependency GetDependency(StrippedDependency d)
        {
            return GetDependency(d.RepoUri, d.Commit);
        }

        public static StrippedDependency GetDependency(string repoUrl, string commit)
        {
            StrippedDependency dep;
            dep = AllDependencies.SingleOrDefault(d => d.RepoUri.ToLowerInvariant() == repoUrl.ToLowerInvariant() && d.Commit.ToLowerInvariant() == commit.ToLowerInvariant());
            if (dep == null)
            {
                dep = new StrippedDependency(repoUrl, commit);
                foreach (StrippedDependency previousDep in AllDependencies.Where(d => d.RepoUri.ToLowerInvariant() == repoUrl.ToLowerInvariant()).SelectMany(d => d.Dependencies))
                {
                    dep.AddDependency(previousDep);
                }
                AllDependencies.Add(dep);
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

        public void AddDependency(StrippedDependency dep)
        {
            StrippedDependency other = GetDependency(dep);
            if (this.Dependencies.Any(d => d.RepoUri.ToLowerInvariant() == other.RepoUri.ToLowerInvariant()))
            {
                return;
            }
            this.Dependencies.Add(other);
            foreach (StrippedDependency sameUrl in AllDependencies.Where(d => d.RepoUri.ToLowerInvariant() == this.RepoUri.ToLowerInvariant()))
            {
                sameUrl.Dependencies.Add(other);
            }
        }

        public void AddDependency(DependencyDetail dep)
        {
            this.AddDependency(GetDependency(dep));
        }

        public bool HasDependencyOn(string repoUrl)
        {
            bool hasDep = false;
            lock (AllDependencies)
            {
                foreach (StrippedDependency dep in this.Dependencies)
                {
                    if (dep.Visited)
                    {
                        return false;
                    }
                    if (dep.RepoUri.ToLowerInvariant() == this.RepoUri.ToLowerInvariant())
                    {
                        return false;
                    }
                    dep.Visited = true;
                    hasDep = hasDep || dep.RepoUri.ToLowerInvariant() == repoUrl.ToLowerInvariant() || dep.HasDependencyOn(repoUrl);
                    if (hasDep)
                    {
                        break;
                    }
                }
                foreach (StrippedDependency dep in AllDependencies)
                {
                    dep.Visited = false;
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
