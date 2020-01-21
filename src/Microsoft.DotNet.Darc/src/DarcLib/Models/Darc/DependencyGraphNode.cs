// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Maestro.Client.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.DarcLib
{
    public class DependencyGraphNode
    {
        public DependencyGraphNode(string repoUri,
                                   string commit,
                                   IEnumerable<DependencyDetail> dependencies,
                                   HashSet<Build> contributingBuilds)
            : this(
                  repoUri,
                  commit,
                  dependencies,
                  new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                  contributingBuilds)
        {
        }

        public DependencyGraphNode(
            string repoUri,
            string commit,
            IEnumerable<DependencyDetail> dependencies,
            HashSet<string> visitedNodes,
            HashSet<Build> contributingBuilds)
        {
            Repository = repoUri;
            Commit = commit;
            Dependencies = dependencies;
            VisitedNodes = new HashSet<string>(visitedNodes, StringComparer.OrdinalIgnoreCase);
            ContributingBuilds = contributingBuilds;
            Children = new HashSet<DependencyGraphNode>(new DependencyGraphNodeComparer());
            Parents = new HashSet<DependencyGraphNode>(new DependencyGraphNodeComparer());
        }

        public HashSet<string> VisitedNodes { get; set; }

        /// <summary>
        ///     Node repository URI
        /// </summary>
        public readonly string Repository;

        /// <summary>
        /// Node commit
        /// </summary>
        public readonly string Commit;

        /// <summary>
        /// Builds that potentially contributed the assets in this node in the graph.
        /// </summary>
        public HashSet<Build> ContributingBuilds { get; set; }

        /// <summary>
        ///     Dependencies of the node at RepoUri and Commit.
        /// </summary>
        public IEnumerable<DependencyDetail> Dependencies { get; set; }

        /// <summary>
        ///     Unique set of repositories that this node is dependent on.
        /// </summary>
        public HashSet<DependencyGraphNode> Children { get; set; }

        public HashSet<DependencyGraphNode> Parents { get; set; }

        /// <summary>
        /// Diff from another node or commit. If null, diff is unknown.
        /// </summary>
        public GitDiff DiffFrom { get; set; }

        public void AddChild(DependencyGraphNode newChild, DependencyDetail dependency)
        {
            Children.Add(newChild);
            newChild.Parents.Add(this);
        }
    }
}
