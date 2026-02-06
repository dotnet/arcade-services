namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

public class BuildAnalysisFileSettings
{
    public string Path { get; set; }
    public string FileName { get; set; }
    public string FilePath => string.Concat(Path, FileName);
}
