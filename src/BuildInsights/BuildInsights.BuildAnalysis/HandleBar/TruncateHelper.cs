// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.PathStructure;

namespace BuildInsights.BuildAnalysis.HandleBar;

public class TruncateHelper : InlineHelper
{
    public override PathInfo Name => "Truncate";

    public override void Invoke(in EncodedTextWriter output, in HelperOptions options, in Context context, in Arguments arguments)
    {
        if (arguments.Length != 2)
        {
            throw new HandlebarsException("{{#truncate}} helper must have two arguments");
        }

        if (arguments[0] is not string input)
        {
            throw new HandlebarsException("{{#truncate}} helper expects first argument to be string");
        }

        if (arguments[1] is not int length || length < 0)
        {
            throw new HandlebarsException("{{#truncate}} helper expects second argument to be a positive integer ");
        }

        int maxTruncateLength = length < input.Length ? length : input.Length;
        output.Write(input.Substring(0, maxTruncateLength));
    }
}
