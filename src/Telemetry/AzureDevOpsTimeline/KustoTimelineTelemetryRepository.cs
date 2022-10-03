// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Kusto.Data.Common;
using Kusto.Data.Exceptions;
using Kusto.Data.Net.Client;
using Kusto.Ingest;
using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Kusto;
using Microsoft.DotNet.Services.Utility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline;

public sealed class KustoTimelineTelemetryRepository : ITimelineTelemetryRepository
{
    private readonly ILogger<KustoTimelineTelemetryRepository> _logger;
    private readonly IKustoIngestClient _ingest;
    private readonly ICslQueryProvider _query;
    private readonly string _database;

    public KustoTimelineTelemetryRepository(ILogger<KustoTimelineTelemetryRepository> logger, IOptionsSnapshot<KustoTimelineTelemetryOptions> options)
    {
        _logger = logger;

        // removing the IngestConnectionString was a default setup in local debugging
        if (string.IsNullOrEmpty(options.Value.IngestConnectionString))
        {
            _logger.LogDebug("No ingest connection string provided; will ignore ingest operations");
            _ingest = new NullKustoIngestClient();
        }
        else
        {
            _ingest = KustoIngestFactory.CreateQueuedIngestClient(options.Value.IngestConnectionString);
        }
        _query = KustoClientFactory.CreateCslQueryProvider(options.Value.QueryConnectionString);
        _database = options.Value.Database;
    }

    public async Task<DateTimeOffset?> GetLatestTimelineBuild(AzureDevOpsProject project)
    {
        try
        {
            using IDataReader result = await _query.ExecuteQueryAsync(
                _database,
                // This isn't user controlled, so I'm not worried about the Kusto injection
                $"""
                    TimelineBuilds
                    | where coalesce(Organization, "dnceng") == '{project.Organization}'
                    | where Project == '{project.Project}'
                    | summarize max(FinishTime)
                    """,
                new ClientRequestProperties()
            );

            // Staging Kusto cluster has no data in the TimelineBuilds table, so we need to handle that case
            if (!result.Read() || result.IsDBNull(0))
            {
                return null;
            }

            return result.GetDateTime(0);
        }
        catch (SemanticException e) when
            (e.SemanticErrors.StartsWith("'where' operator: Failed to resolve") && (
                e.SemanticErrors.EndsWith("'Organization'") ||
                e.SemanticErrors.EndsWith("'Project'")))
        {
            // A column isn't there, we probably reinitalized the tables
            return null;
        }
    }

    public async Task WriteTimelineBuilds(IEnumerable<AugmentedBuild> augmentedBuilds, string organization)
    {
        await KustoHelpers.WriteDataToKustoInMemoryAsync(
            _ingest,
            _database,
            "TimelineBuilds",
            _logger,
            augmentedBuilds,
            b => new[]
            {
                new KustoValue("BuildId", b.Build.Id, KustoDataType.Int),
                new KustoValue("Status", b.Build.Status, KustoDataType.String),
                new KustoValue("Result", b.Build.Result, KustoDataType.String),
                new KustoValue("Repository", b.Build.Repository?.Name ?? b.Build.Repository?.Id, KustoDataType.String),
                new KustoValue("Reason", b.Build.Reason, KustoDataType.String),
                new KustoValue("BuildNumber", b.Build.BuildNumber, KustoDataType.String),
                new KustoValue("QueueTime", b.Build.QueueTime, KustoDataType.DateTime),
                new KustoValue("StartTime", b.Build.StartTime, KustoDataType.DateTime),
                new KustoValue("FinishTime", b.Build.FinishTime, KustoDataType.DateTime),
                new KustoValue("Project", b.Build.Project?.Name, KustoDataType.String),
                new KustoValue("DefinitionId", b.Build.Definition?.Id.ToString(), KustoDataType.String),
                new KustoValue("Definition", $"{b.Build.Definition?.Path}\\{b.Build.Definition?.Name}", KustoDataType.String),
                new KustoValue("SourceBranch", GitHelpers.NormalizeBranchName(b.Build.SourceBranch), KustoDataType.String),
                new KustoValue("TargetBranch", GitHelpers.NormalizeBranchName(b.TargetBranch), KustoDataType.String),
                new KustoValue("Organization", organization, KustoDataType.String),
            });
    }

    public async Task WriteTimelineIssues(IEnumerable<AugmentedTimelineIssue> issues)
    {
        await KustoHelpers.WriteDataToKustoInMemoryAsync(
            _ingest,
            _database,
            "TimelineIssues",
            _logger,
            issues,
            b => new[]
            {
                new KustoValue("BuildId", b.BuildId, KustoDataType.Int),
                new KustoValue("RecordId", b.RecordId, KustoDataType.String),
                new KustoValue("TimelineId", b.TimelineId, KustoDataType.String),
                new KustoValue("Index", b.Index, KustoDataType.Int),
                new KustoValue("Path", b.AugmentedIndex, KustoDataType.String),
                new KustoValue("Type", b.Raw.Type, KustoDataType.String),
                new KustoValue("Category", b.Raw.Category, KustoDataType.String),
                new KustoValue("Message", b.Raw.Message, KustoDataType.String),
                new KustoValue("Bucket", b.Bucket, KustoDataType.String),
            });
    }

    public async Task WriteTimelineRecords(IEnumerable<AugmentedTimelineRecord> records)
    {
        await KustoHelpers.WriteDataToKustoInMemoryAsync(
            _ingest,
            _database,
            "TimelineRecords",
            _logger,
            records,
            b => new[]
            {
                new KustoValue("BuildId", b.BuildId, KustoDataType.Int),
                new KustoValue("RecordId", b.Raw.Id, KustoDataType.String),
                new KustoValue("TimelineId", b.TimelineId, KustoDataType.String),
                new KustoValue("Order", b.Raw.Order, KustoDataType.Int),
                new KustoValue("Path", b.AugmentedOrder, KustoDataType.String),
                new KustoValue("ParentId", b.Raw.ParentId, KustoDataType.String),
                new KustoValue("Name", b.Raw.Name, KustoDataType.String),
                new KustoValue("StartTime", b.Raw.StartTime, KustoDataType.DateTime),
                new KustoValue("FinishTime", b.Raw.FinishTime, KustoDataType.DateTime),
                new KustoValue("Result", b.Raw.Result, KustoDataType.String),
                new KustoValue("ResultCode", b.Raw.ResultCode, KustoDataType.String),
                new KustoValue("ChangeId", b.Raw.ChangeId, KustoDataType.Int),
                new KustoValue("LastModified", b.Raw.LastModified, KustoDataType.DateTime),
                new KustoValue("WorkerName", b.Raw.WorkerName, KustoDataType.String),
                new KustoValue("Details", b.Raw.Details?.Url, KustoDataType.String),
                new KustoValue("ErrorCount", b.Raw.ErrorCount, KustoDataType.Int),
                new KustoValue("WarningCount", b.Raw.WarningCount, KustoDataType.Int),
                new KustoValue("Url", b.Raw.Url, KustoDataType.String),
                new KustoValue("LogId", b.Raw.Log?.Id, KustoDataType.Int),
                new KustoValue("LogUri", b.Raw.Log?.Url, KustoDataType.String),
                new KustoValue("TaskId", b.Raw.Task?.Id, KustoDataType.Int),
                new KustoValue("TaskName", b.Raw.Task?.Name, KustoDataType.String),
                new KustoValue("TaskVersion", b.Raw.Task?.Version, KustoDataType.String),
                new KustoValue("Attempt", b.Raw.Attempt, KustoDataType.Int),
                new KustoValue("ImageName", b.ImageName, KustoDataType.String),
            });
    }

    public async Task WriteTimelineValidationMessages(IEnumerable<(int buildId, BuildRequestValidationResult validationResult)> validationResults)
    {
        await KustoHelpers.WriteDataToKustoInMemoryAsync(
            _ingest,
            _database,
            "TimelineIssues",
            _logger,
            validationResults,
            b => new[]
            {
                new KustoValue("BuildId", b.buildId, KustoDataType.Int),
                new KustoValue("RecordId", null, KustoDataType.String),
                new KustoValue("Index", null, KustoDataType.Int),
                new KustoValue("Path", null, KustoDataType.String),
                new KustoValue("Type", b.validationResult.Result, KustoDataType.String),
                new KustoValue("Category", "ValidationResult", KustoDataType.String),
                new KustoValue("Message", b.validationResult.Message, KustoDataType.String),
                new KustoValue("Bucket", "ValidationResult", KustoDataType.String),
            });
    }

    public void Dispose()
    {
        _ingest.Dispose();
        _query.Dispose();
    }
}
