using System;
using System.Collections.Generic;
using System.Text;

namespace RolloutScorer.Models
{
    public class RolloutWeightConfig
    {
        public int RolloutMinutesPerPoint { get; set; }
        public int PointsPerIssue { get; set; }
        public int PointsPerHotfix { get; set; }
        public int PointsPerRollback { get; set; }
        public int DowntimeMinutesPerPoint { get; set; }
        public int FailurePoints { get; set; }
    }
}
