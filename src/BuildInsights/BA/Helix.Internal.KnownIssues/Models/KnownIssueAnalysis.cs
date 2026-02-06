using System;
using System.Collections.Generic;
using Azure;
using Azure.Data.Tables;

namespace Microsoft.Internal.Helix.KnownIssues.Models
{
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
}
