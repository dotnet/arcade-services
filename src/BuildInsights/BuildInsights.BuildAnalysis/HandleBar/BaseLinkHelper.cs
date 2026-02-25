// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;

namespace BuildInsights.BuildAnalysis.HandleBar;

public abstract class BaseLinkHelper : InlineHelper
{
    public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
    {
        if (arguments.Length > 1 && arguments[1] != null)
        {
            string urlString = arguments[1].ToString()!;

            RenderString(output, urlString, arguments[0]?.ToString());
        }
    }

    protected abstract void RenderString(EncodedTextWriter output, string url, string? text);
}
