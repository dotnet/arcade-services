using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace DotNet.Status.Web.TeamsMessages
{
    // "Classic" MessageCard Schema reference:
    //   https://docs.microsoft.com/en-us/outlook/actionable-messages/message-card-reference
    //
    // There are more possible properties than those used here.
    //
    // Note that the JSON representation expects camelCase naming convention.

    public class MessageCard
    {
        [JsonProperty("@context")]
        public string Context { get; set; } = "https://schema.org/extensions";

        [JsonProperty("@type")]
        public string Type { get; set; } = "MessageCard";

        public string ThemeColor { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        [JsonProperty("potentialAction")]
        public IList<IAction> Actions { get; set; } = Array.Empty<IAction>();

        public IList<Section> Sections { get; set; } = Array.Empty<Section>();
    }
}
