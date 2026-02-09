// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace BuildInsights.BuildAnalysis.Models.Views;

public class LinkToTestResultsView
{
    public LinkToTestResultsView(string link, string name)
    {
        Link = link;
        Name = name;
    }

    public string Link { get; }
    public string Name { get; }
}
