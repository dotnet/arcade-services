// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.PathStructure;

namespace BuildInsights.BuildAnalysis.HandleBar;

public class MarkdownLinkHelper : BaseLinkHelper
{
    public override PathInfo Name => "LinkTo";

    protected override void RenderString(EncodedTextWriter output, string url, string? text)
    {
        output.WriteSafeString("[");
        output.Write(text);
        output.WriteSafeString("](");
        output.Write(url);
        output.WriteSafeString(")");
    }
}
