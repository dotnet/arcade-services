// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class CodeflowHistory
    {
        public CodeflowHistory(
            List<CodeflowGraphCommit> forwardFlowHistory,
            List<CodeflowGraphCommit> backflowHistory,
            string repoName,
            string vmrName,
            bool resultIsOutdated)
        {
            ForwardFlowHistory = forwardFlowHistory;
            BackflowHistory = backflowHistory;
            RepoName = repoName;
            VmrName = vmrName;
            ResultIsOutdated = resultIsOutdated;
        }

        [JsonProperty("forwardFlowHistory")]
        public List<CodeflowGraphCommit> ForwardFlowHistory { get; }

        [JsonProperty("backflowHistory")]
        public List<CodeflowGraphCommit> BackflowHistory { get; }

        [JsonProperty("repoName")]
        public string RepoName { get; }

        [JsonProperty("vmrName")]
        public string VmrName { get; }

        [JsonProperty("resultIsOutdated")]
        public bool ResultIsOutdated { get; }
    }

    public partial class CodeflowGraphCommit
    {
        public CodeflowGraphCommit(
            string commitSha,
            string author,
            string description,
            string sourceRepoFlowSha)
        {
            CommitSha = commitSha;
            Author = author;
            Description = description;
            SourceRepoFlowSha = sourceRepoFlowSha;
        }

        [JsonProperty("commitSha")]
        public string CommitSha { get; }

        [JsonProperty("author")]
        public string Author { get; }

        [JsonProperty("description")]
        public string Description { get; }

        [JsonProperty("sourceRepoFlowSha")]
        public string SourceRepoFlowSha { get; }
    }
}
