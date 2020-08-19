using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class FlowRef
    {
        public FlowRef(double officialBuildTime, double prBuildTime, bool onLongestBuildPath, double bestCasePathTime, double worstCasePathTime, int goalTimeInMinutes, string repository, string branch, string id)
        {
            OfficialBuildTime = officialBuildTime;
            PrBuildTime = prBuildTime;
            OnLongestBuildPath = onLongestBuildPath;
            BestCasePathTime = bestCasePathTime;
            WorstCasePathTime = worstCasePathTime;
            GoalTimeInMinutes = goalTimeInMinutes;
            Repository = repository;
            Branch = branch;
            Id = id;
        }

        [JsonProperty("repository")]
        public string Repository { get; }

        [JsonProperty("branch")]
        public string Branch { get; }

        [JsonProperty("id")]
        public string Id { get; }

        [JsonProperty("officialBuildTime")]
        public double OfficialBuildTime { get; }

        [JsonProperty("prBuildTime")]
        public double PrBuildTime { get; }

        [JsonProperty("onLongestBuildPath")]
        public bool OnLongestBuildPath { get; set; }

        [JsonProperty("bestCasePathTime")]
        public double BestCasePathTime { get; set; }

        [JsonProperty("worstCasePathTime")]
        public double WorstCasePathTime { get; set; }

        [JsonProperty("goalTimeInMinutes")]
        public int GoalTimeInMinutes { get; set; }

        [JsonProperty("inputChannels")]
        public HashSet<string> InputChannels { get; set; }

        [JsonProperty("outputChannels")]
        public HashSet<string> OutputChannels { get; set; }
    }
}
