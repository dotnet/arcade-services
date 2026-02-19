// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.PathStructure;

namespace BuildInsights.BuildAnalysis.HandleBar;

public class SnapshotIdCommentHelper : InlineHelper
{
    public override PathInfo Name => "SnapshotIdComment";

    public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
    {
        if (arguments.Length < 1)
        {
            return;
        }

        output.WriteSafeString($"<!-- SnapshotId: {arguments[0]} -->");
    }
}
