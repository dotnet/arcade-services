// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.PathStructure;

namespace BuildInsights.BuildAnalysis.HandleBar;

public class RenderKnownLinks : InlineHelper
{
    public override PathInfo Name => "RenderKnownLinks";

    public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
    {
        if (arguments.Length != 1)
        {
            throw new HandlebarsException($"{{{{#{Name}}}}} helper must have one argument");
        }

        if (arguments[0] is not string errorMessage)
        {
            throw new HandlebarsException($"{{{{#{Name}}}}} helper expects argument to be string");
        }

        if (RewriteConsoleLogLink(in output, errorMessage))
        {
            return;
        }
        else if (RewriteFailureLogLink(in output, errorMessage))
        {
            return;
        }
        else
        {
            output.Write(errorMessage);
        }
    }

    private static bool RewriteConsoleLogLink(in EncodedTextWriter output, string errorMessage)
    {
        string consoleLogPrefix = "Check the Test tab or this console log: ";

        int markerStartIndex = errorMessage.IndexOf(consoleLogPrefix);

        if (markerStartIndex < 0)
        {
            return false;
        }

        int urlStartIndex = markerStartIndex + consoleLogPrefix.Length;

        output.Write(errorMessage[..markerStartIndex]);
        output.WriteSafeString($"<a href=\"{errorMessage[urlStartIndex..]}\">Check the Test tab or [this console log]</a>");

        return true;
    }

    private static bool RewriteFailureLogLink(in EncodedTextWriter output, string errorMessage)
    {
        string failureLogPrefix = "Failure log: ";

        int markerStartIndex = errorMessage.IndexOf(failureLogPrefix);

        if (markerStartIndex < 0)
        {
            return false;
        }

        int urlStartIndex = markerStartIndex + failureLogPrefix.Length;

        output.Write(errorMessage[..markerStartIndex]);
        output.WriteSafeString($"<a href=\"{errorMessage[urlStartIndex..]}\">[Failure log]</a>");

        return true;
    }
}
