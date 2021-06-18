using System;
using System.Collections.Generic;
using System.Text;

namespace RolloutScorer.Models
{
    public class ScorecardBuildBreakdown
    {
        public BuildSummary BuildSummary { get; }
        public Scorecard Score { get; }
        public ScorecardBuildBreakdown(BuildSummary buildSummary)
        {
            BuildSummary = buildSummary;
            Score = new Scorecard();
        }
    }
}
