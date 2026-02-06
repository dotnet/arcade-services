namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class FailingConfiguration
    {
        /// <summary>
        /// ex. Windows.10.Amd64.Open
        /// </summary>
        public Configuration Configuration { get; set; }
        public string TestLogs { get; set; }
        public string HistoryLink { get; set; }
        public string ArtifactLink { get; set; }
    }

    public class Configuration
    {
        public string Name { get; set; }
        public string Url { get; set; }

        public Configuration()
        {
        }

        public Configuration(string name, string organization, string project, TestCaseResult testCaseResult)
        {
            Name = name;
            Url = $"https://dev.azure.com/{organization}/{project}/_build/results?buildId={testCaseResult.BuildId}&view=ms.vss-test-web.build-test-results-tab&runId={testCaseResult.TestRunId}&resultId={testCaseResult.Id}";
        }
    }
}
