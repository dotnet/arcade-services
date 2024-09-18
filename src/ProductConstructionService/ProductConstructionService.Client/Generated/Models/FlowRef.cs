// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
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
        public List<string> InputChannels { get; set; }

        [JsonProperty("outputChannels")]
        public List<string> OutputChannels { get; set; }
    }
}
