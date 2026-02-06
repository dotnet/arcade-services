namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public class HelixWorkItem
    {
        public string HelixJobId { get; set; }
        public string HelixWorkItemName { get; set; }
        public string ConsoleLogUrl { get; set; }
        public int? ExitCode { get; set; }
        public string Status { get; set; }
    }
}
