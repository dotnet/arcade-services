// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using HandlebarsDotNet;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Models.Views;
using BuildInsights.BuildAnalysis.HandleBar;

namespace BuildInsights.BuildAnalysis;

public interface IMarkdownGenerator
{
    string GenerateMarkdown(MarkdownParameters parameters);
    string GenerateEmptyMarkdown(UserSentimentParameters sentiment);
    string GenerateMarkdown(BuildAnalysisUpdateOverridenResult result);
}

public class MarkdownGenerator : IMarkdownGenerator
{
    private readonly IHandlebars _hb = Handlebars.Create(new HandlebarsConfiguration { TextEncoder = new MarkdownEncoder() });
    private readonly Dictionary<string, HandlebarsTemplate<object, object>> _templates;

    public MarkdownGenerator(HandlebarHelpers helpers)
    {
        helpers.AddHelpers(_hb);
        _templates = Templates.Compile(_hb);
    }

    public string GenerateMarkdown(BasicResultsView results)
    {
        if (results.HasData)
        {
            return _templates["BuildResultAnalysis"](results);
        }
        else
        {
            return _templates["NoResults"](results);
        }
    }

    public string GenerateMarkdown(BuildAnalysisUpdateOverridenResult result)
    {
        return _templates["OverridenCheckResult"](result);
    }

    public string GenerateMarkdown(MarkdownParameters parameters)
    {
        if (parameters == null)
        {
            return _templates["NoResults"](null!);
        }

        return GenerateMarkdown(new ConsolidatedBuildResultAnalysisView(parameters));
    }

    public string GenerateEmptyMarkdown(UserSentimentParameters sentiment)
    {
        return GenerateMarkdown(new BasicResultsView(sentiment));
    }
}
