// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models;

public class Link
{
    public string Name { get; }
    public string Url { get; }

    public Link(string name, string url)
    {
        Name = name;
        Url = url;
    }
}
