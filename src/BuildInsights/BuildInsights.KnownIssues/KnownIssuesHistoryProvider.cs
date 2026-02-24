// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using BuildInsights.Data;
using BuildInsights.Data.Models;
using BuildInsights.KnownIssues.Models;
using Microsoft.EntityFrameworkCore;

#nullable enable
namespace BuildInsights.KnownIssues;

public interface IKnownIssuesHistoryService
{
    Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int id);
    Task<List<KnownIssueAnalysis>> GetKnownIssuesHistory(string issueRepo, long issueId, DateTimeOffset since, CancellationToken cancellationToken);
    Task SaveKnownIssueError(string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken);
    Task<KnownIssueError?> GetLatestKnownIssueError(string issueRepo, long issueId, CancellationToken cancellationToken);
    Task SaveBuildKnownIssueValidation(int buildId, string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken);
    Task<List<KnownIssueAnalysis>> GetBuildKnownIssueValidatedRecords(string buildId, string issueRepo, long issueId, CancellationToken cancellationToken);
}

public class KnownIssuesHistoryProvider : IKnownIssuesHistoryService
{
    private readonly BuildInsightsContext _buildInsightsDb;

    public KnownIssuesHistoryProvider(BuildInsightsContext buildInsightsDb)
    {
        _buildInsightsDb = buildInsightsDb;
    }

    public async Task SaveKnownIssuesHistory(IEnumerable<KnownIssue> knownIssues, int buildId)
    {
        IEnumerable<KnownIssueAnalysis> analysisList = knownIssues
            .Select(ki => new KnownIssueAnalysis
            {
                ErrorMessage = KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(ki.BuildError),
                BuildId = buildId,
                IssueId = NormalizeIssueId(ki.GitHubIssue.RepositoryWithOwner, ki.GitHubIssue.Id),
            });

        await _buildInsightsDb.KnownIssueAnalysis.AddRangeAsync(analysisList);
        await _buildInsightsDb.SaveChangesAsync();
    }

    public async Task<List<KnownIssueAnalysis>> GetKnownIssuesHistory(string issueRepo, long issueId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        string normalizedIssueId = NormalizeIssueId(issueRepo, issueId);

        return await _buildInsightsDb.KnownIssueAnalysis
            .Where(a => a.IssueId == normalizedIssueId && a.Timestamp > since)
            .ToListAsync(cancellationToken);
    }

    public async Task<KnownIssueError?> GetLatestKnownIssueError(string issueRepo, long issueId, CancellationToken cancellationToken)
    {
        string repository = NormalizeRepository(issueRepo);
        string issueIdString = issueId.ToString();

        return await _buildInsightsDb.KnownIssueErrors
            .FirstOrDefaultAsync(a => a.Repository == repository && a.IssueId == issueIdString, cancellationToken);
    }

    public async Task SaveKnownIssueError(string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken)
    {
        string repository = NormalizeRepository(issueRepo);
        string issueIdString = issueId.ToString();

        var existingError = await _buildInsightsDb.KnownIssueErrors
            .FirstOrDefaultAsync(e => e.Repository == repository && e.IssueId == issueIdString, cancellationToken);

        if (existingError != null)
        {
            existingError.ErrorMessage = KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(errorMessages);
            existingError.Timestamp = DateTimeOffset.UtcNow;
        }
        else
        {
            var knownIssueError = new KnownIssueError
            {
                Repository = repository,
                IssueId = issueIdString,
                ErrorMessage = KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(errorMessages),
                Timestamp = DateTimeOffset.UtcNow
            };
            await _buildInsightsDb.KnownIssueErrors.AddAsync(knownIssueError, cancellationToken);
        }

        await _buildInsightsDb.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<KnownIssueAnalysis>> GetBuildKnownIssueValidatedRecords(string buildId, string issueRepo, long issueId, CancellationToken cancellationToken)
    {
        string normalizedIssueId = NormalizeIssueId(issueRepo, issueId);

        return await _buildInsightsDb.KnownIssueAnalysis
            .Where(a => a.IssueId == normalizedIssueId && a.BuildId.ToString() == buildId)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveBuildKnownIssueValidation(int buildId, string issueRepo, long issueId, List<string> errorMessages, CancellationToken cancellationToken)
    {
        string normalizedIssueId = NormalizeIssueId(issueRepo, issueId);

        var existingRecord = await _buildInsightsDb.KnownIssueAnalysis
            .FirstOrDefaultAsync(a => a.IssueId == normalizedIssueId && a.BuildId == buildId, cancellationToken);

        if (existingRecord != null)
        {
            existingRecord.ErrorMessage = KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(errorMessages);
            existingRecord.Timestamp = DateTimeOffset.UtcNow;
        }
        else
        {
            var knownIssueAnalysis = new KnownIssueAnalysis
            {
                ErrorMessage = KnownIssueHelper.GetKnownIssueErrorMessageStringConversion(errorMessages),
                BuildId = buildId,
                IssueId = normalizedIssueId,
                Timestamp = DateTimeOffset.UtcNow
            };
            await _buildInsightsDb.KnownIssueAnalysis.AddAsync(knownIssueAnalysis, cancellationToken);
        }

        await _buildInsightsDb.SaveChangesAsync(cancellationToken);
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
