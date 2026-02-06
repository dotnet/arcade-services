using System.Collections.Immutable;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services
{
    public interface IMarkdownGenerator
    {
        string GenerateMarkdown(MarkdownParameters parameters);
        string GenerateEmptyMarkdown(UserSentimentParameters sentiment);
        string GenerateMarkdown(BuildAnalysisUpdateOverridenResult result);
    }
}
