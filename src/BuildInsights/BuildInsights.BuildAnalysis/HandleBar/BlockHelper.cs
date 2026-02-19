// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using HandlebarsDotNet.Helpers;
using HandlebarsDotNet.PathStructure;

namespace BuildInsights.BuildAnalysis.HandleBar;

public abstract class BlockHelper : IHelperDescriptor<BlockHelperOptions>
{
    public abstract PathInfo Name { get; }

    public object Invoke(in BlockHelperOptions options, in Context context, in Arguments arguments)
    {
        return this.ReturnInvoke(options, context, arguments);
    }

    public abstract void Invoke(
        in EncodedTextWriter output,
        in BlockHelperOptions options,
        in Context context,
        in Arguments arguments);
}
