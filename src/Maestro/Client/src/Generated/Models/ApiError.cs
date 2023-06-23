// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class ApiError
    {
        public ApiError()
        {
        }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("errors")]
        public IImmutableList<string> Errors { get; set; }
    }
}
