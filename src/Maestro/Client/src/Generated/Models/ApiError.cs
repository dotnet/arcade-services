using System;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Maestro.Client.Models
{
    public partial class ApiError
    {
        public ApiError(string message, IImmutableList<string> errors)
        {
            Message = message;
            Errors = errors;
        }

        [JsonProperty("message")]
        public string Message { get; }

        [JsonProperty("errors")]
        public IImmutableList<string> Errors { get; }
    }
}
