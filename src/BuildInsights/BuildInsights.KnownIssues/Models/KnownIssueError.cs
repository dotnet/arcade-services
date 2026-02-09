// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Azure;
using Azure.Data.Tables;

namespace BuildInsights.KnownIssues.Models;

public class KnownIssueError : ITableEntity
{
    public KnownIssueError() { }

    public KnownIssueError(string repository, string issueId, string errorMessage)
    {
        PartitionKey = repository;
        RowKey = issueId;
        ErrorMessage = errorMessage;
    }

    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public string ErrorMessage { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}
