// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using BuildInsights.BuildAnalysis;
using HandlebarsDotNet;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using BuildInsights.BuildAnalysis.Models;
using BuildInsights.BuildAnalysis.Models.Views;
using BuildInsights.BuildAnalysis.Services;

namespace BuildInsights.BuildAnalysis.Providers;

public class MarkdownGenerator : IMarkdownGenerator
{
    private readonly ILogger<MarkdownGenerator> _logger;
    private readonly IHandlebars _hb = Handlebars.Create(new HandlebarsConfiguration { TextEncoder = new MarkdownEncoder() });
    private readonly Dictionary<string, HandlebarsTemplate<object, object>> _templates;

    public MarkdownGenerator(
        HandlebarHelpers helpers,
        ILogger<MarkdownGenerator> logger)
    {
        _logger = logger;
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
            return _templates["NoResults"](null);
        }

        return GenerateMarkdown(new ConsolidatedBuildResultAnalysisView(parameters));
    }

    public string GenerateEmptyMarkdown(UserSentimentParameters sentiment)
    {
        return GenerateMarkdown(new BasicResultsView(sentiment));
    }
}
