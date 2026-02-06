using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models.Views
{
    public class AttemptView
    {
        public string CheckSig { get; set; }
        public string LinkToBuild { get; set; }
        public int AttemptId { get; set; }
        public List<StepResultView> BuildStepsResult { get; set; } = new List<StepResultView>();
        public List<TestResultView> TestResults { get; set; } = new List<TestResultView>();
        public bool HasTestFailures => TestResults != null && TestResults.Count > 0;
        public bool HasBuildFailures => BuildStepsResult != null && BuildStepsResult.Count > 0;
    }
}
