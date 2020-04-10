// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.DotNet.DarcLib.Actions.Clone
{
    public class SkipDependencyExplorationExplanation
    {
        public SkipDependencyExplorationReason Reason { get; set; }

        public string Details { get; set; }

        public override string ToString() => $"{Reason}: {Details}";

        public string ToGraphVizColor()
        {
            switch (Reason)
            {
                case SkipDependencyExplorationReason.AlreadyVisited:
                    return "#69725E";
                case SkipDependencyExplorationReason.SelfDependency:
                    return "#66A2A4";
                case SkipDependencyExplorationReason.CircularWhenOnlyConsideringName:
                    return "#227084";
                case SkipDependencyExplorationReason.Ignored:
                    return "#354362";
                case SkipDependencyExplorationReason.DependencyDetailMissingCommit:
                    return "#D15838";
                default:
                    return null;
            }
        }
    }
}
