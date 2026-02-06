using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class ArtifactDetailsFile
    {
        [JsonPropertyName("items")]
        public List<Item> Items { get; set; }
    }

    public class Item
    {
        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("blob")]
        public Blob Blob { get; set; }

    }

    public class Blob
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("size")]
        public int Size { get; set; }
    }
}
