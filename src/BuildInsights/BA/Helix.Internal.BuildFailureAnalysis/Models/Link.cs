namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class Link
    {
        public string Name { get; }
        public string Url { get; }

        public Link(string name, string url)
        {
            Name = name;
            Url = url;
        }
    }
}
