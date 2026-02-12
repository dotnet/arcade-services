// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis;

[Serializable]
public class BuildNotFoundException : Exception
{
    public BuildNotFoundException()
    {
    }

    public BuildNotFoundException(string message)
        : base(message)
    {
    }

    public BuildNotFoundException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
