using System;
using Azure;
using Azure.Data.Tables;

namespace Microsoft.Internal.Helix.KnownIssues.Models;

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
