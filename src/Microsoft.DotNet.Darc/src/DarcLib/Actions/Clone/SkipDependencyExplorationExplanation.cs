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
                    return "lightslategrey";
                case SkipDependencyExplorationReason.SelfDependency:
                    return "goldenrod";
                case SkipDependencyExplorationReason.CircularWhenOnlyConsideringName:
                    return "purple";
                case SkipDependencyExplorationReason.Ignored:
                    return "gray";
                case SkipDependencyExplorationReason.DependencyDetailMissingCommit:
                    return "rosybrown1";
                default:
                    return null;
            }
        }
    }
}
