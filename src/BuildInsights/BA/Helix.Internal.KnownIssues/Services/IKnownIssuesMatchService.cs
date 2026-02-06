using Microsoft.Internal.Helix.KnownIssues.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Microsoft.Internal.Helix.KnownIssues.Services;

public interface IKnownIssuesMatchService
{
    Task<List<KnownIssue>> GetKnownIssuesInStream(Stream stream, IReadOnlyList<KnownIssue> knownIssues);
    List<KnownIssue> GetKnownIssuesInString(string errorLine, IReadOnlyList<KnownIssue> knownIssues);
}
