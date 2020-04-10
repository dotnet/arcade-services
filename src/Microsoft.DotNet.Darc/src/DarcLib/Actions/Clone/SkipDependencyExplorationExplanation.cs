// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class SkipDependencyExplorationExplanation
    {
        public SkipDependencyExplorationReason Reason { get; set; }

        public string Details { get; set; }

        public override string ToString() => $"{Reason}: {Details}";

        public string ToGraphVizAttributes()
        {
            switch (Reason)
            {
                case SkipDependencyExplorationReason.AlreadyVisited:
                    return "color=\"#69725E\" arrowhead=empty";
                case SkipDependencyExplorationReason.SelfDependency:
                    return "color=\"#D29900\" arrowhead=diamond";
                case SkipDependencyExplorationReason.CircularWhenOnlyConsideringName:
                    return "color=\"#BBB422\" arrowhead=ediamond";
                case SkipDependencyExplorationReason.Ignored:
                    return "color=\"#41BAB8\" arrowhead=odot";
                case SkipDependencyExplorationReason.DependencyDetailMissingCommit:
                    return "color=\"#F53C3C\"";
                default:
                    return null;
            }
        }
    }
}
