using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class CreateBranchRequest
    {
        public CreateBranchRequest()
        {
        }

        [JsonProperty("subscriptionId")]
        public Guid? SubscriptionId { get; set; }

        [JsonProperty("buildId")]
        public int? BuildId { get; set; }

        [JsonProperty("targetBranch")]
        public string TargetBranch { get; set; }
    }
}
