using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Services
{
    public interface IHelixDataService
    {
        bool IsHelixWorkItem(string comment);
        Task<HelixWorkItem> TryGetHelixWorkItem(string workItemInfo, CancellationToken cancellationToken);
        Task<Dictionary<string, List<HelixWorkItem>>> TryGetHelixWorkItems(ImmutableList<string> workItemInfo, CancellationToken cancellationToken);
    }
}
