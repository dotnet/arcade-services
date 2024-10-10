// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class AzDoBuild
    {
        public AzDoBuild(DateTimeOffset finishTime, int id, string result)
        {
            FinishTime = finishTime;
            Id = id;
            Result = result;
        }

        [JsonProperty("finishTime")]
        public DateTimeOffset FinishTime { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("result")]
        public string Result { get; set; }

        [JsonIgnore]
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrEmpty(Result))
                {
                    return false;
                }
                return true;
            }
        }
    }
}
