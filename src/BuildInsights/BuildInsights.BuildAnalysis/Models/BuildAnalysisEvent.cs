// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;

namespace BuildInsights.BuildAnalysis.Models;

public class BuildAnalysisEvent : ITableEntity
{
    public BuildAnalysisEvent() { }

    public BuildAnalysisEvent(string pipelineName, int buildId, string repo, string project, DateTimeOffset analysisTimestamp, bool isRepositorySupported = true)
    {
        PartitionKey = pipelineName;
        RowKey = buildId.ToString();
        Repository = repo;
        Project = project;
        AnalysisTimestamp = analysisTimestamp;
        IsRepositorySupported = isRepositorySupported;
    }
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public string Repository { get; set; }
    public string Project { get; set; }
    public bool IsRepositorySupported { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public DateTimeOffset? AnalysisTimestamp { get; set; }
    public ETag ETag { get; set; }
}
