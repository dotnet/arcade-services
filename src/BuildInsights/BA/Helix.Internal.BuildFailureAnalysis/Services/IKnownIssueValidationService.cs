using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services;

public interface IKnownIssueValidationService
{
    Task ValidateKnownIssue(KnownIssueValidationMessage knownIssueValidationMessage, CancellationToken cancellationToken);
}
