// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Maestro.Web.Api.v2019_01_16.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.DotNet.DarcLib;
using Newtonsoft.Json;

namespace Maestro.Web.Api.v2018_07_16.Models
{
    public class FlowRef
    {
        [JsonConstructor]
        public FlowRef(string id, string repository, string branch)
        {
            Id = id;
            Repository = repository;
            Branch = branch;
        }

        public FlowRef(string id, string repository, string branch, double officialBuildTime, double prBuildTime)
        {
            Id = id;
            Repository = repository;
            Branch = branch;
            OfficialBuildTime = officialBuildTime;
            PRBuildTime = prBuildTime;
        }

        public FlowRef(
            string id, 
            string repository, 
            string branch, 
            double officialBuildTime, 
            double prBuildTime,
            bool onLongestBuildPath,
            double bestCase,
            double worstCase,
            int goalTime,
            HashSet<string> inputChannels,
            HashSet<string> outputChannels)
        {
            Id = id;
            Repository = repository;
            Branch = branch;
            OfficialBuildTime = officialBuildTime;
            PRBuildTime = prBuildTime;
            OnLongestBuildPath = onLongestBuildPath;
            BestCasePathTime = bestCase;
            WorstCasePathTime = worstCase;
            GoalTimeInMinutes = goalTime;
            InputChannels = inputChannels;
            OutputChannels = outputChannels;
        }

        public static FlowRef Create(DependencyFlowNode other)
        {
            return new FlowRef(
                other.Id,
                other.Repository,
                other.Branch,
                other.OfficialBuildTime,
                other.PrBuildTime,
                other.OnLongestBuildPath,
                other.BestCasePathTime,
                other.WorstCasePathTime,
                other.GoalTimeInMinutes,
                other.InputChannels,
                other.OutputChannels);
        }

        public string Repository { get; }
        public string Branch { get; }
        public string Id { get; }
        public double OfficialBuildTime { get; }
        public double PRBuildTime { get; }
        public bool OnLongestBuildPath { get; set; }
        public double BestCasePathTime { get; set; }
        public double WorstCasePathTime { get; set; }
        public int GoalTimeInMinutes { get; set; }
        public HashSet<string> InputChannels { get; set; }
        public HashSet<string> OutputChannels { get; set; }
    }
}
