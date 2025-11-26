// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace ProductConstructionService.Client.Models
{
    public partial class Namespace
    {
        public Namespace(int id, string name)
        {
            Id = id;
            Name = name;
        }

        [JsonProperty("id")]
        public int Id { get; }

        [JsonProperty("name")]
        public string Name { get; }
    }
}
