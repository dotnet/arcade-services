// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure;
using Azure.Data.Tables;

namespace BuildInsights.KnownIssues.Models;

public class KnownIssueAnalysis : ITableEntity
{
    public KnownIssueAnalysis() { }
    public KnownIssueAnalysis(string errorMessages, int buildId, string issueId)
    {
        ErrorMessage = errorMessages;
        PartitionKey = issueId;
        RowKey = buildId.ToString();
    }

    public string ErrorMessage { get; set; }
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
