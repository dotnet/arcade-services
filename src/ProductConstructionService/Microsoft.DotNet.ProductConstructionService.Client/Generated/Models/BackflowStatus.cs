// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class BackflowStatus
    {
        public BackflowStatus(string vmrCommitSha, DateTimeOffset computationTimestamp, IImmutableDictionary<string, BranchBackflowStatus> branchStatuses)
        {
            VmrCommitSha = vmrCommitSha;
            ComputationTimestamp = computationTimestamp;
            BranchStatuses = branchStatuses;
        }

        [JsonProperty("vmrCommitSha")]
        public string VmrCommitSha { get; set; }

        [JsonProperty("computationTimestamp")]
        public DateTimeOffset ComputationTimestamp { get; set; }

        [JsonProperty("branchStatuses")]
        public IImmutableDictionary<string, BranchBackflowStatus> BranchStatuses { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(VmrCommitSha))
                {
                    return false;
                }
                if (BranchStatuses == default(IImmutableDictionary<string, BranchBackflowStatus>))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
