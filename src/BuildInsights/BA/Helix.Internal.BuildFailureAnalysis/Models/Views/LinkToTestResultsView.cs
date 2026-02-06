namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models.Views
{
    public class LinkToTestResultsView
    {
        public LinkToTestResultsView(string link, string name)
        {
            Link = link;
            Name = name;
        }

        public string Link { get; }
        public string Name { get; }
    }
}
