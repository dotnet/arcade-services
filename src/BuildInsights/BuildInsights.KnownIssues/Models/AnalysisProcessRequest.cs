// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BuildInsights.KnownIssues.Models;

public class AnalysisProcessRequest
{
    [JsonPropertyName("issueId")]
    public long IssueId { get; set; }

    [JsonPropertyName("repository")]
    public string Repository { get; set; }
}
