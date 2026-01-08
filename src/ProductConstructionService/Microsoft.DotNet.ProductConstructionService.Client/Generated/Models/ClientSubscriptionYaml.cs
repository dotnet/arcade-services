// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class ClientSubscriptionYaml
    {
        public ClientSubscriptionYaml(Guid id, bool enabled, string channel, string sourceRepository, string targetRepository, string targetBranch, Models.ClientUpdateFrequency updateFrequency, bool batchable, bool sourceEnabled)
        {
            Id = id;
            Enabled = enabled;
            Channel = channel;
            SourceRepository = sourceRepository;
            TargetRepository = targetRepository;
            TargetBranch = targetBranch;
            UpdateFrequency = updateFrequency;
            Batchable = batchable;
            SourceEnabled = sourceEnabled;
        }

        [JsonProperty("id")]
        public Guid Id { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("sourceRepository")]
        public string SourceRepository { get; set; }

        [JsonProperty("targetRepository")]
        public string TargetRepository { get; set; }

        [JsonProperty("targetBranch")]
        public string TargetBranch { get; set; }

        [JsonProperty("updateFrequency")]
        public ClientUpdateFrequency UpdateFrequency { get; set; }

        [JsonProperty("batchable")]
        public bool Batchable { get; set; }

        [JsonProperty("excludedAssets")]
        public IImmutableList<string> ExcludedAssets { get; set; }

        [JsonProperty("mergePolicies")]
        public IImmutableList<Models.ClientMergePolicyYaml> MergePolicies { get; set; }

        [JsonProperty("failureNotificationTags")]
        public string FailureNotificationTags { get; set; }

        [JsonProperty("sourceEnabled")]
        public bool SourceEnabled { get; set; }

        [JsonProperty("sourceDirectory")]
        public string SourceDirectory { get; set; }

        [JsonProperty("targetDirectory")]
        public string TargetDirectory { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Channel))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(SourceRepository))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(TargetRepository))
                {
                    return false;
                }
                if (string.IsNullOrEmpty(TargetBranch))
                {
                    return false;
                }
                if (UpdateFrequency == default(Models.ClientUpdateFrequency))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
