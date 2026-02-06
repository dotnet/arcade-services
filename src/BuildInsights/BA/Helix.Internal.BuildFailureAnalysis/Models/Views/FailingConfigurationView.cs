namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models.Views
{
    public class FailingConfigurationView
    {
        public string Configuration { get; set; } // ex. Windows.10.Amd64.Open
        public string TestLogs { get; set; }
        public string HistoryLink { get; set; }
        public string ArtifactLink { get; set; } = "https://example.test";
    }
}
