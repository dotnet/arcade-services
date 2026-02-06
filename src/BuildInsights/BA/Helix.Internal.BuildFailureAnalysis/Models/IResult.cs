using System.Collections.Immutable;
using Microsoft.Internal.Helix.KnownIssues.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models
{
    public interface IResult
    {
        public FailureRate FailureRate { get; }
        public IImmutableList<KnownIssue> KnownIssues { get; }
    }
}
