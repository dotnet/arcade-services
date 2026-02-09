// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Azure;
using Azure.Data.Tables;

namespace BuildInsights.BuildAnalysis.Models;

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
