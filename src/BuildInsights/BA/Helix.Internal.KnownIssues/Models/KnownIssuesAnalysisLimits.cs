namespace Microsoft.Internal.Helix.KnownIssues.Models;

public class KnownIssuesAnalysisLimits
{
    public int RecordCountLimit { get; set; }
    public int LogLinesCountLimit { get; set; }
    public int FailingTestCountLimit { get; set; }
    public int HelixLogsFilesLimit { get; set; }
}
