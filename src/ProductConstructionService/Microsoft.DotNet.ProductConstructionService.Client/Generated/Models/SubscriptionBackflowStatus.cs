// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class SubscriptionBackflowStatus
    {
        public SubscriptionBackflowStatus(string targetRepository, string targetBranch, string lastBackflowedSha, int commitDistance, Guid subscriptionId)
        {
            TargetRepository = targetRepository;
            TargetBranch = targetBranch;
            LastBackflowedSha = lastBackflowedSha;
            CommitDistance = commitDistance;
            SubscriptionId = subscriptionId;
        }

        [JsonProperty("targetRepository")]
        public string TargetRepository { get; set; }

        [JsonProperty("targetBranch")]
        public string TargetBranch { get; set; }

        [JsonProperty("lastBackflowedSha")]
        public string LastBackflowedSha { get; set; }

        [JsonProperty("commitDistance")]
        public int CommitDistance { get; set; }

        [JsonProperty("subscriptionId")]
        public Guid SubscriptionId { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(TargetRepository))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(TargetBranch))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(LastBackflowedSha))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
