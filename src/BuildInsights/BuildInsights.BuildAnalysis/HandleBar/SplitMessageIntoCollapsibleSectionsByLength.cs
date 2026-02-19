// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.PathStructure;

namespace BuildInsights.BuildAnalysis.HandleBar;

public class SplitMessageIntoCollapsibleSectionsByLength : InlineHelper
{
    public override PathInfo Name => "SplitMessageIntoCollapsibleSectionsByLength";

    public override void Invoke(
        in EncodedTextWriter output,
        in HelperOptions options,
        in Context context,
        in Arguments arguments)
    {
        if (arguments.Length != 2)
        {
            throw new HandlebarsException($"{{{{#{Name}}}}} helper must have two arguments");
        }

        if (arguments[0] is not string errorMessage)
        {
            throw new HandlebarsException($"{{{{#{Name}}}}} helper expects argument to be string");
        }

        if (arguments[1] is not int splitLength)
        {
            throw new HandlebarsException($"{{{{#{Name}}}}} helper expects argument to be int");
        }

        if (errorMessage.Length <= splitLength)
        {
            var renderKnown = new RenderKnownLinks();
            renderKnown.Invoke(output, options, context, new Arguments(arguments[0]));
        }
        else
        {
            output.WriteSafeString("<i>expand to see the full error</i>");
            output.WriteSafeString("<ul><details><summary>");
            output.Write(errorMessage[..splitLength]);
            output.WriteSafeString("</summary>");
            output.Write(errorMessage[splitLength..]);
            output.WriteSafeString("</details></ul>");
        }
    }
}
