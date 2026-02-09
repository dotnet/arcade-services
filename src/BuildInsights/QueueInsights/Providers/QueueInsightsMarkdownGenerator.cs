// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.UserSentiment;
using HandlebarsDotNet;
using QueueInsights.Models;
using QueueInsights.Services;

namespace QueueInsights.Providers;

public class QueueInsightsMarkdownGenerator : IQueueInsightsMarkdownGenerator
{
    private readonly IHandlebars _hb = Handlebars.Create();
    private readonly Dictionary<string, HandlebarsTemplate<object, object>> _templates;

    public QueueInsightsMarkdownGenerator(SentimentInjectorFactory sentimentInjectorFactory)
    {
        InsightMarkdownHelpers.RegisterHelpers(_hb, sentimentInjectorFactory);
        InsightMarkdownHelpers.RegisterFormatters(_hb);
        _templates = InsightMarkdownHelpers.Compile(_hb);
    }

    public string GenerateMarkdown(MarkdownView view)
    {
        return _templates["HelixQueueInsights"](view);
    }

    public string GeneratePendingMarkdown(string repo, string commitHash, string pullRequest)
    {
        return _templates["QueueInsightsPending"](new UserSentimentParameters(repo, commitHash, pullRequest, true));
    }
}
