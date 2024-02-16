using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class FlowRef
    {
        public FlowRef(double officialBuildTime, double prBuildTime, bool onLongestBuildPath, double bestCasePathTime, double worstCasePathTime, int goalTimeInMinutes)
        {
            OfficialBuildTime = officialBuildTime;
            PrBuildTime = prBuildTime;
            OnLongestBuildPath = onLongestBuildPath;
            BestCasePathTime = bestCasePathTime;
            WorstCasePathTime = worstCasePathTime;
            GoalTimeInMinutes = goalTimeInMinutes;
        }

        [JsonProperty("repository")]
        public string Repository { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("officialBuildTime")]
        public double OfficialBuildTime { get; set; }

        [JsonProperty("prBuildTime")]
        public double PrBuildTime { get; set; }

        [JsonProperty("onLongestBuildPath")]
        public bool OnLongestBuildPath { get; set; }

        [JsonProperty("bestCasePathTime")]
        public double BestCasePathTime { get; set; }

        [JsonProperty("worstCasePathTime")]
        public double WorstCasePathTime { get; set; }

        [JsonProperty("goalTimeInMinutes")]
        public int GoalTimeInMinutes { get; set; }

        [JsonProperty("inputChannels")]
        public IImmutableList<string> InputChannels { get; set; }

        [JsonProperty("outputChannels")]
        public IImmutableList<string> OutputChannels { get; set; }
    }
}
