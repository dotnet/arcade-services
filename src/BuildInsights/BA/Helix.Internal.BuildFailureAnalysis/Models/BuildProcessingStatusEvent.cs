using System;
using Azure;
using Azure.Data.Tables;

namespace Microsoft.Internal.Helix.BuildFailureAnalysis.Models;

public class BuildProcessingStatusEvent : ITableEntity
{
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public string Status { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public BuildProcessingStatusEvent() { }

    public BuildProcessingStatusEvent(string repository, int buildId, BuildProcessingStatus processingStatus)
    {
        PartitionKey = repository;
        RowKey = buildId.ToString();
        Status = processingStatus.Value;
    }
}
