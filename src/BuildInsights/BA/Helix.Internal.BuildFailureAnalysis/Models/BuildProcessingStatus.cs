namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

public class BuildProcessingStatus
{
    public string Value { get; }

    public BuildProcessingStatus(string status)
    {
        Value = status;
    }

    public static BuildProcessingStatus InProcess => new BuildProcessingStatus("InProcess");
    public static BuildProcessingStatus Completed => new BuildProcessingStatus("Completed");
    public static BuildProcessingStatus ConclusionOverridenByUser => new BuildProcessingStatus("ConclusionOverridenByUser");
}
