namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

public class KnownIssueValidationResult
{
    private KnownIssueValidationResult(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static readonly KnownIssueValidationResult
        Matched = new("**Result validation:** :white_check_mark: Known issue matched with the provided build.");

    public static readonly KnownIssueValidationResult NotMatched =
        new("**Result validation:** :x: Known issue did not match with the provided build.");

    public static readonly KnownIssueValidationResult ValidationFailed =
        new("**Result validation:** :warning: There was a problem performing the validation. Contact [.NET Engineering Services Team](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/890/How-to-get-a-hold-of-Engineering-Servicing) and share this issue.");

    public static readonly KnownIssueValidationResult MissingBuild =
        new(@"**Result validation:** :warning: Validation could not be done without an Azure DevOps build URL on the issue. Please add it to the ""**Build:** :mag_right:"" line.");

    public static readonly KnownIssueValidationResult BuildNotFound =
        new(@"**Result validation:** :warning: Provided build not found. Provide a valid build in the ""**Build:** :mag_right:"" line.");

    public static readonly KnownIssueValidationResult BuildInformationNotFound =
        new("**Result validation:** :warning: Build internal information not found. This may happen if your build is too old. Please use a build that is no older than two weeks. If the problem persists, contact [.NET Engineering Services Team](https://dev.azure.com/dnceng/internal/_wiki/wikis/DNCEng%20Services%20Wiki/890/How-to-get-a-hold-of-Engineering-Servicing) and share this issue.");

    public static KnownIssueValidationResult UnableToCreateKnownIssue(string exceptionMessage)
    {
        return new KnownIssueValidationResult($"**Result validation:** :x: There was a problem creating a known issue. Check the exception message and proceed accordingly: `{exceptionMessage}`");
    }
}
