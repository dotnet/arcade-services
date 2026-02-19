// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.PathStructure;

namespace BuildInsights.BuildAnalysis.HandleBar;

public class HtmlLinkHelper : BaseLinkHelper
{
    public override PathInfo Name => "LinkToHtml";

    protected override void RenderString(EncodedTextWriter output, string url, string text)
    {
        // TODO: text isn't technically "safe" here, but it's sort of half safe (links don't work, but other things do)
        output.WriteSafeString($"<a href=\"{url}\">{text}</a>");
    }
}
