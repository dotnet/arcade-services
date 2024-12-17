// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Newtonsoft.Json;

namespace Microsoft.DotNet.ProductConstructionService.Client.Models
{
    public partial class BuildUpdate
    {
        public BuildUpdate()
        {
        }

        [JsonProperty("released")]
        public bool? Released { get; set; }
    }
}
