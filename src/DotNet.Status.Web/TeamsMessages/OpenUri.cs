using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DotNet.Status.Web.TeamsMessages
{
    public class OpenUri : IAction
    {
        [JsonProperty("@type")]
        public string Type { get; set; } = "OpenUri";

        public string Name { get; set; } = "Open comment";

        public IList<Target> Targets { get; set; } = Array.Empty<Target>();
    }
}