// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure;
using Azure.Data.Tables;
using BuildInsights.Data.Models;
using BuildInsights.KnownIssues.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildInsights.KnownIssues;

public interface IKnownIssuesHistoryService
{
    Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int id);
    Task<List<KnownIssueAnalysis>> GetKnownIssuesHistory(string issueRepo, long issueId, DateTimeOffset since, CancellationToken cancellationToken);
    Task SaveKnownIssueError(string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken);
    Task<KnownIssueError> GetLatestKnownIssueError(string issueRepo, long issueId, CancellationToken cancellationToken);
    Task SaveBuildKnownIssueValidation(int buildId, string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken);
    Task<List<KnownIssueAnalysis>> GetBuildKnownIssueValidatedRecords(string buildId, string issueRepo, long issueId, CancellationToken cancellationToken);
}

public class KnownIssuesHistoryProvider : IKnownIssuesHistoryService
{
    private readonly KnownIssuesErrorsTableConnectionSettings _knownIssuesTableOptions;
    private readonly KnownIssueValidationTableConnectionSettings _knownIssueValidationTable;
    private readonly ILogger _logger;

    public KnownIssuesHistoryProvider(
        IOptions<KnownIssuesErrorsTableConnectionSettings> knownIssuesTableOptions,
        IOptions<KnownIssueValidationTableConnectionSettings> knownIssueValidationTable,
        ILogger<KnownIssuesHistoryProvider> logger)
    {
        _logger = logger;
        _knownIssuesTableOptions = knownIssuesTableOptions.Value;
        _knownIssueValidationTable = knownIssueValidationTable.Value;
    }

    public async Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int buildId)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_tableOptions.Name, _tableOptions.Endpoint);
        IEnumerable<KnownIssueAnalysis> analysisList = knownIssues
            .Select(ki => new KnownIssueAnalysis(
                KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(ki.BuildError),
                buildId,
                NormalizeIssueId(ki.GitHubIssue.RepositoryWithOwner, ki.GitHubIssue.Id)));

        foreach (KnownIssueAnalysis knownIssueAnalysis in analysisList)
        {
            await tableClient.UpsertEntityAsync(knownIssueAnalysis);
        }
    }

    public async Task<List<KnownIssueAnalysis>> GetKnownIssuesHistory(string issueRepo, long issueId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        string normalizedIssueId = NormalizeIssueId(issueRepo, issueId);
        List<KnownIssueAnalysis> knownIssueHistory = new List<KnownIssueAnalysis>();
        TableClient tableClient = _tableClientFactory.GetTableClient(_tableOptions.Name, _tableOptions.Endpoint);

        AsyncPageable<KnownIssueAnalysis> results = tableClient.QueryAsync<KnownIssueAnalysis>(
            a => a.PartitionKey == normalizedIssueId && a.Timestamp > since, 100,
            cancellationToken: cancellationToken);

        await foreach (Page<KnownIssueAnalysis> page in results.AsPages())
        {
            knownIssueHistory.AddRange(page.Values);
        }
        return knownIssueHistory;
    }

    public async Task<KnownIssueError> GetLatestKnownIssueError(string issueRepo, long issueId, CancellationToken cancellationToken)
    {
        string repository = NormalizeRepository(issueRepo);
        TableClient tableClient = _tableClientFactory.GetTableClient(_knownIssuesTableOptions.Name, _knownIssuesTableOptions.Endpoint);

        AsyncPageable<KnownIssueError> results = tableClient.QueryAsync<KnownIssueError>(
            a => a.PartitionKey == repository && a.RowKey == issueId.ToString(), 100,
            cancellationToken: cancellationToken);

        var knownIssueErrors = new List<KnownIssueError>();
        await foreach (Page<KnownIssueError> page in results.AsPages().WithCancellation(cancellationToken))
        {
            knownIssueErrors.AddRange(page.Values);
        }

        //We are expecting only one record ever.
        return knownIssueErrors.FirstOrDefault();
    }

    public async Task SaveKnownIssueError(string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_knownIssuesTableOptions.Name, _knownIssuesTableOptions.Endpoint);

        string repository = NormalizeRepository(issueRepo);
        var knownIssueError = new KnownIssueError(repository, issueId.ToString(), KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(errorMessages));
        await tableClient.UpsertEntityAsync(knownIssueError, cancellationToken: cancellationToken);
    }

    public async Task<List<KnownIssueAnalysis>> GetBuildKnownIssueValidatedRecords(string buildId, string issueRepo, long issueId, CancellationToken cancellationToken)
    {
        string normalizedIssueId = NormalizeIssueId(issueRepo, issueId);

        TableClient tableClient = _tableClientFactory.GetTableClient(_knownIssueValidationTable.Name, _knownIssueValidationTable.Endpoint);

        AsyncPageable<KnownIssueAnalysis> results = tableClient.QueryAsync<KnownIssueAnalysis>(
            a => a.PartitionKey == normalizedIssueId && a.RowKey == buildId, 100, 
            cancellationToken: cancellationToken);

        List<KnownIssueAnalysis> validatedKnownIssues = new List<KnownIssueAnalysis>();
        await foreach (Page<KnownIssueAnalysis> page in results.AsPages().WithCancellation(cancellationToken))
        {
            validatedKnownIssues.AddRange(page.Values);
        }

        return validatedKnownIssues;
    }

    public async Task SaveBuildKnownIssueValidation(int buildId, string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken)
    {
        TableClient tableClient = _tableClientFactory.GetTableClient(_knownIssueValidationTable.Name, _knownIssueValidationTable.Endpoint);

        KnownIssueAnalysis knownIssueAnalysis = new KnownIssueAnalysis(KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(errorMessages), buildId, NormalizeIssueId(issueRepo, issueId));

        await tableClient.UpsertEntityAsync(knownIssueAnalysis, cancellationToken: cancellationToken);
    }

    private static string NormalizeRepository(string repository)
    {
        return $"{repository.Replace('/', '.')}";
    }

    public static string NormalizeIssueId(string repository, long id)
    {
        return $"{repository.Replace('/', '.')}.{id}";
    }
}
