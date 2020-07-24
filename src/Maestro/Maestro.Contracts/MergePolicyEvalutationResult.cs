using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Maestro.Contracts
{
    public class MergePolicyEvaluationResult
    {
        public MergePolicyEvaluationResult(IEnumerable<SingleResult> results)
        {
            Results = results.ToImmutableList();
        }

        public IReadOnlyList<SingleResult> Results { get; }

        public bool Succeeded => Results.Count > 0 && Results.All(r => r.Success == true);

        public bool Pending => Results.Count > 0 && Results.Any(r => r.Success == null);

        public bool Failed => Results.Count > 0 && Results.Any(r => r.Success == false);

        public class SingleResult
        {
            public SingleResult(bool? success, string message, string mergePolicyName, string mergePolicyDisplayName)
            {
                Success = success;
                Message = message;
                MergePolicyName = mergePolicyName;
                MergePolicyDisplayName = mergePolicyDisplayName;
            }

            public bool? Success { get; }
            public string Message { get; }
            public string MergePolicyName { get; }
            public string MergePolicyDisplayName { get; }
        }
    }
}
