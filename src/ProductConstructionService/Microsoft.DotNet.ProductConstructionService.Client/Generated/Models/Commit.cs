// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class Commit
    {
        public Commit()
        {
        }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("sha")]
        public string Sha { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
