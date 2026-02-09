// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis.Services;

public interface IMarkdownGenerator
{
    string GenerateMarkdown(MarkdownParameters parameters);
    string GenerateEmptyMarkdown(UserSentimentParameters sentiment);
    string GenerateMarkdown(BuildAnalysisUpdateOverridenResult result);
}
