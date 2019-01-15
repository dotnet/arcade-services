using Newtonsoft.Json;
using System;

namespace Microsoft.DotNet.DarcLib
{
    public partial class AzureDevOpsReleaseDefinition
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("artifacts")]
        public AzureDevOpsArtifact[] Artifacts { get; set; }

        [JsonProperty("environments")]
        public object[] Environments { get; set; }

        [JsonProperty("revision")]
        public long Revision { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("createdBy")]
        public object CreatedBy { get; set; }

        [JsonProperty("createdOn")]
        public DateTimeOffset CreatedOn { get; set; }

        [JsonProperty("modifiedBy")]
        public object ModifiedBy { get; set; }

        [JsonProperty("modifiedOn")]
        public DateTimeOffset ModifiedOn { get; set; }

        [JsonProperty("isDeleted")]
        public bool IsDeleted { get; set; }

        [JsonProperty("variables")]
        public object Variables { get; set; }

        [JsonProperty("variableGroups")]
        public long[] VariableGroups { get; set; }

        [JsonProperty("triggers")]
        public object[] Triggers { get; set; }

        [JsonProperty("releaseNameFormat")]
        public string ReleaseNameFormat { get; set; }

        [JsonProperty("tags")]
        public string[] Tags { get; set; }

        [JsonProperty("properties")]
        public object Properties { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("projectReference")]
        public object ProjectReference { get; set; }

        [JsonProperty("url")]
        public Uri Url { get; set; }

        [JsonProperty("_links")]
        public object Links { get; set; }
    }
}
