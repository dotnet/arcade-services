// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class SourceBuildGraph
    {
        public static SourceBuildGraph Create(
            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> upstreamMap)
        {
            // Flip the upstream graph for ascending lookups.
            var downstreamMap = upstreamMap
                .SelectMany(entry => entry.Value.Select(dep => new
                {
                    Repo = entry.Key,
                    Downstream = dep
                }))
                .GroupBy(v => v.Downstream, v => v.Repo, SourceBuildIdentity.CaseInsensitiveComparer)
                .ToDictionary(v => v.Key, v => v.ToArray());

            return new SourceBuildGraph(
                downstreamMap.Keys
                    .Union(upstreamMap.Keys, SourceBuildIdentity.CaseInsensitiveComparer)
                    .OrderBy(n => n.RepoUri, StringComparer.Ordinal)
                    .ToArray(),
                upstreamMap,
                downstreamMap);
        }

        public IReadOnlyList<SourceBuildIdentity> Nodes { get; }

        public Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> Upstreams { get; }
        public Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> Downstreams { get; }

        private SourceBuildGraph(
            IReadOnlyList<SourceBuildIdentity> nodes,
            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> upstreams,
            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> downstreams)
        {
            Nodes = nodes;
            Upstreams = upstreams;
            Downstreams = downstreams;
        }

        public string ToGraphVizString()
        {
            var sb = new StringBuilder("digraph G { rankdir=LR;\n");

            foreach (var n in Nodes)
            {
                sb.Append("\"");
                sb.Append(n);
                sb.Append("\"");

                if (Upstreams.TryGetValue(n, out var upstreams))
                {
                    sb.Append(" -> {");
                    foreach (var u in upstreams)
                    {
                        sb.Append("\"");
                        sb.Append(u);
                        sb.Append("\";");
                    }
                    sb.Append("}");
                }

                sb.AppendLine();
            }

            sb.AppendLine("}");

            return sb.ToString();
        }

        public IEnumerable<SourceBuildIdentity> GetDownstreams(SourceBuildIdentity node) =>
            Downstreams.TryGetValue(node, out var values)
                ? values
                : Enumerable.Empty<SourceBuildIdentity>();

        public IEnumerable<SourceBuildIdentity> GetUpstreams(SourceBuildIdentity node) =>
            Upstreams.TryGetValue(node, out var values)
                ? values
                : Enumerable.Empty<SourceBuildIdentity>();

        public IEnumerable<SourceBuildIdentity> GetAllDownstreams(SourceBuildIdentity node) =>
            GetTraverseListCore(node, Downstreams);

        public IEnumerable<SourceBuildIdentity> GetAllUpstreams(SourceBuildIdentity node) =>
            GetTraverseListCore(node, Upstreams);

        private IEnumerable<SourceBuildIdentity> GetTraverseListCore(
            SourceBuildIdentity start,
            Dictionary<SourceBuildIdentity, SourceBuildIdentity[]> links)
        {
            var visited = new HashSet<SourceBuildIdentity>(SourceBuildIdentity.CaseInsensitiveComparer);
            var next = new Queue<SourceBuildIdentity>();
            next.Enqueue(start);

            while (next.Any())
            {
                SourceBuildIdentity node = next.Dequeue();

                if (!links.TryGetValue(node, out var linkedNodes))
                {
                    continue;
                }

                foreach (var linkedNode in linkedNodes)
                {
                    if (!visited.Add(linkedNode))
                    {
                        continue;
                    }

                    yield return linkedNode;
                    next.Enqueue(linkedNode);
                }
            }
        }

        public static SourceBuildGraph Create(JObject obj)
        {
            var nodes = obj[nameof(Nodes)].Values<JObject>().Select(SourceBuildIdentity.Create).ToArray();
            var upstreams = obj["Upstreams"].Values<JObject>()
                .Select(o => new
                {
                    Key = o.Value<int>("Key"),
                    Values = o["Values"].ToObject<int[]>()
                })
                .ToDictionary(
                    p => nodes[p.Key],
                    p => p.Values.Select(i => nodes[i]).ToArray());

            return Create(upstreams);
        }

        public JObject ToJObject()
        {
            int GetIndex(SourceBuildIdentity node) => Nodes.TakeWhile(n => n != node).Count();

            return JObject.FromObject(new
            {
                Nodes = Nodes.Select(n => n.ToJObject()).ToArray(),
                Upstreams = Upstreams
                    .Select(pair => new
                    {
                        Key = GetIndex(pair.Key),
                        Values = pair.Value.Select(GetIndex).ToArray()
                    })
                    .ToArray()
            });
        }
    }
}
