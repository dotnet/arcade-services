// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using BuildInsights.BuildAnalysis.Models;
using Microsoft.DotNet.Helix.Client;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Logging;

namespace BuildInsights.BuildAnalysis;

public interface IHelixDataService
{
    bool IsHelixWorkItem(string comment);
    Task<HelixWorkItem?> TryGetHelixWorkItem(string workItemInfo, CancellationToken cancellationToken);
    Task<Dictionary<string, List<HelixWorkItem>>> TryGetHelixWorkItems(IEnumerable<string> workItemInfo, CancellationToken cancellationToken);
}

public class HelixDataProvider : IHelixDataService
{
    private readonly IKustoClientProvider _kustoClientProvider;
    private readonly IHelixApi _helixApi;
    private readonly ILogger<HelixDataProvider> _logger;

    //https://learn.microsoft.com/en-us/azure/data-explorer/kusto/concepts/querylimits#limit-on-query-complexity
    private const int LimitQueryComplexity = 25;

    public HelixDataProvider(
        IKustoClientProvider kustoClientProvider,
        IHelixApi helixApi,
        ILogger<HelixDataProvider> logger)
    {
        _kustoClientProvider = kustoClientProvider;
        _helixApi = helixApi;
        _logger = logger;
    }

    public bool IsHelixWorkItem(string comment)
    {
        return TryGetHelixWorkItemFromComment(comment, out _);
    }

    public async Task<HelixWorkItem?> TryGetHelixWorkItem(string workItemInfo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(workItemInfo)) return null;

        try
        {
            if (!TryGetHelixWorkItemFromComment(workItemInfo, out HelixWorkItem? helixWorkItem))
            {
                return null;
            }

            WorkItemInformation workItemDetails = await GetHelixWorkItemDetails(helixWorkItem.HelixWorkItemName,
                helixWorkItem.HelixJobId, cancellationToken);
            helixWorkItem.ConsoleLogUrl = workItemDetails?.ConsoleLogUrl;
            helixWorkItem.ExitCode = workItemDetails?.ExitCode;
            helixWorkItem.Status = workItemDetails?.Status;

            return helixWorkItem;
        }
        catch
        {
            _logger.LogWarning($"Unable to process work item info {workItemInfo}");
            return null;
        }
    }

    public async Task<Dictionary<string, List<HelixWorkItem>>> TryGetHelixWorkItems(
        IEnumerable<string> workItemsInfo,
        CancellationToken cancellationToken)
    {
        var helixWorkItem = new Dictionary<string, List<HelixWorkItem>>();
        var relationCommentWorkItemKey = new Dictionary<string, string>();
        foreach (string workItemInfo in workItemsInfo)
        {
            if (TryGetHelixWorkItemFromComment(workItemInfo, out HelixWorkItem? workItem))
            {
                helixWorkItem[GetKeyForHelixWorkItem(workItem)] = [workItem];
                relationCommentWorkItemKey[workItemInfo] = GetKeyForHelixWorkItem(workItem);
            }
        }

        List<HelixWorkItem> helixWorkItemsOnKusto = await KustoWorkItemInformation(helixWorkItem.SelectMany(h => h.Value).ToList());
        foreach (HelixWorkItem workItemResult in helixWorkItemsOnKusto.Where(r => !string.IsNullOrEmpty(r.ConsoleLogUrl)))
        {
            string keyForHelixWorkItem = GetKeyForHelixWorkItem(workItemResult);
            List<HelixWorkItem> helixWorkItems = helixWorkItem.GetValueOrDefault(keyForHelixWorkItem, [])
                .Where(t => !string.IsNullOrEmpty(t.ConsoleLogUrl))
                .ToList();
            helixWorkItems.Add(workItemResult);

            helixWorkItem[keyForHelixWorkItem] = helixWorkItems;
        }

        IEnumerable<HelixWorkItem> helixWorkItemsNotFoundOnKusto = helixWorkItem.Values.SelectMany(h => h.Where(t => t.ConsoleLogUrl == null));
        List<HelixWorkItem> helixWorkItemOnSql = await HelixApiWorkItemInformation(helixWorkItemsNotFoundOnKusto, cancellationToken);
        foreach (HelixWorkItem workItemResult in helixWorkItemOnSql.Where(t => !string.IsNullOrEmpty(t.ConsoleLogUrl)))
        {
            string keyForHelixWorkItem = GetKeyForHelixWorkItem(workItemResult);
            List<HelixWorkItem> helixWorkItems = helixWorkItem.GetValueOrDefault(keyForHelixWorkItem, [])
                .Where(t => !string.IsNullOrEmpty(t.ConsoleLogUrl))
                .ToList();
            helixWorkItems.Add(workItemResult);
            helixWorkItem[keyForHelixWorkItem] = helixWorkItems;
        }

        return MatchHelixWorkItemsWithComments(helixWorkItem, relationCommentWorkItemKey);
    }

    private async Task<WorkItemInformation> GetHelixWorkItemDetails(
        string workItemName,
        string helixJobName,
        CancellationToken cancellationToken)
    {
        return await KustoWorkItemInformation(workItemName, helixJobName)
            ?? await HelixApiWorkItemInformation(workItemName, helixJobName, cancellationToken);
    }

    private async Task<WorkItemInformation?> KustoWorkItemInformation(string workItemName, string helixJobName)
    {
        var query = new KustoQuery(
            """
             WorkItems
            | where FriendlyName == _workItem
            | where JobName == _job
            | project ExitCode, ConsoleUri, Status
            """);
        query.AddParameter("_workItem", workItemName, KustoDataType.String);
        query.AddParameter("_job", helixJobName, KustoDataType.String);
        IDataReader reader = await _kustoClientProvider.ExecuteKustoQueryAsync(query);

        return !reader.Read() ? null : new WorkItemInformation(reader.GetString(1), reader.GetInt32(0), reader.GetString(2));
    }

    private async Task<WorkItemInformation> HelixApiWorkItemInformation(
        string workItemName,
        string helixJobName,
        CancellationToken cancellationToken)
    {
        var workItemDetails = await _helixApi.WorkItem.DetailsAsync(workItemName, helixJobName, cancellationToken);
        return new WorkItemInformation(workItemDetails.ConsoleOutputUri, workItemDetails.ExitCode, workItemDetails.State);
    }

    private async Task<List<HelixWorkItem>> KustoWorkItemInformation(List<HelixWorkItem> helixWorkItems)
    {
        if (helixWorkItems.Count == 0)
        {
            return [];
        }

        var workItemInformation = new List<HelixWorkItem>();
        for (int i = 0; i < Math.Ceiling(helixWorkItems.Count / (double)LimitQueryComplexity); i++)
        {
            List<HelixWorkItem> recordsToQuery = [..helixWorkItems.Skip(LimitQueryComplexity * i).Take(LimitQueryComplexity)];
            KustoQuery kustoQuery = CreateKustoQueryForHelixWorkItems(recordsToQuery);
            IDataReader reader = await _kustoClientProvider.ExecuteKustoQueryAsync(kustoQuery);

            while (reader.Read())
            {
                workItemInformation.Add(new HelixWorkItem
                {
                    HelixJobId = reader.GetString(0),
                    HelixWorkItemName = reader.GetString(1),
                    ExitCode = reader.GetInt32(2),
                    ConsoleLogUrl = reader.GetString(3)
                });
            }
        }

        return workItemInformation;
    }

    private static KustoQuery CreateKustoQueryForHelixWorkItems(List<HelixWorkItem> helixWorkItems)
    {
        var query = new KustoQuery();
        var queryText = new StringBuilder();
        queryText.AppendLine("WorkItems");

        int count = 0;
        foreach (HelixWorkItem workItem in helixWorkItems)
        {
            queryText.AppendLine(count == 0
                ? $"| where FriendlyName == _workItem{count} and JobName == _job{count}"
                : $"or FriendlyName == _workItem{count} and JobName == _job{count}");

            query.AddParameter($"_workItem{count}", workItem.HelixWorkItemName, KustoDataType.String);
            query.AddParameter($"_job{count}", workItem.HelixJobId, KustoDataType.String);

            count++;
        }

        queryText.AppendLine("| project JobName, FriendlyName, ExitCode, ConsoleUri");
        query.Text = queryText.ToString();
        return query;
    }

    private static Task<List<HelixWorkItem>> HelixApiWorkItemInformation(
        IEnumerable<HelixWorkItem> helixWorkItems,
        CancellationToken cancellationToken)
    {
        // TODO: Use a new API call to get the loguri/exitcode metadata for multiple workitems at once
        return Task.FromResult<List<HelixWorkItem>>([]);
    }

    //private async Task<List<HelixWorkItem>> SqlWorkItemInformation(List<HelixWorkItem> helixWorkItems,
    //  CancellationToken cancellationToken)
    //{
    //    if (helixWorkItems.Count == 0) return [];

    //    List<WorkItemSqlResult> sqlQueryResultForWorkItems = [];
    //    for (int i = 0; i < Math.Ceiling(helixWorkItems.Count / (double)LimitQueryComplexity); i++)
    //    {
    //        List<HelixWorkItem> recordsToQuery = helixWorkItems.Skip(LimitQueryComplexity * i).Take(LimitQueryComplexity).ToList();
    //        sqlQueryResultForWorkItems.AddRange(await GetSqlQueryResultsForWorkItems(recordsToQuery, cancellationToken));
    //    }

    //    sqlQueryResultForWorkItems = await GetSqlQueryResultsForWorkItems(helixWorkItems, cancellationToken);

    //    foreach (HelixWorkItem helixWorkItem in helixWorkItems)
    //    {
    //        List<WorkItemSqlResult> workItemQueryResult = sqlQueryResultForWorkItems.Where(t =>
    //            t.JobName.Equals(helixWorkItem.HelixJobId)
    //            && t.WorkItemName.Equals(helixWorkItem.HelixWorkItemName)).ToList();

    //        string consoleLogValue = string.Empty;
    //        string exitCodeValue = string.Empty;

    //        foreach (WorkItemSqlResult result in workItemQueryResult)
    //        {
    //            switch (result.EventName)
    //            {
    //                case "LogUri":
    //                    consoleLogValue = result.EventValue;
    //                    break;
    //                case "ExitCode":
    //                    exitCodeValue = result.EventValue;
    //                    break;
    //            }
    //        }

    //        int? exitCode = null;
    //        if (!string.IsNullOrEmpty(exitCodeValue) && int.TryParse(exitCodeValue, out int parsedExitCode))
    //        {
    //            exitCode = parsedExitCode;
    //        }

    //        helixWorkItem.ConsoleLogUrl = consoleLogValue;
    //        helixWorkItem.ExitCode = exitCode;
    //    }

    //    return helixWorkItems;
    //}

    private static bool TryGetHelixWorkItemFromComment(string comment, [NotNullWhen(true)] out HelixWorkItem? helixWorkItem)
    {
        helixWorkItem = null;
        if (string.IsNullOrEmpty(comment)) return false;

        try
        {
            helixWorkItem = JsonSerializer.Deserialize<HelixWorkItem>(comment);
            if (helixWorkItem == null || string.IsNullOrEmpty(helixWorkItem.HelixWorkItemName) || string.IsNullOrEmpty(helixWorkItem.HelixJobId))
            {
                helixWorkItem = null;
                return false;
            }

            return true;
        }
        catch
        {
            helixWorkItem = null;
            return false;
        }
    }

    public static string GetKeyForHelixWorkItem(HelixWorkItem helixWorkItem)
    {
        return $"{helixWorkItem.HelixWorkItemName}/{helixWorkItem.HelixJobId}";
    }

    private static Dictionary<string, List<HelixWorkItem>> MatchHelixWorkItemsWithComments(
        Dictionary<string, List<HelixWorkItem>> helixWorkItems,
        IReadOnlyDictionary<string, string> relationCommentWorkItemKey)
    {
        var matchHelixWorkItemWithComments = new Dictionary<string, List<HelixWorkItem>>();

        foreach (KeyValuePair<string, string> commentWorkItemKey in relationCommentWorkItemKey)
        {
            if (helixWorkItems.TryGetValue(commentWorkItemKey.Value, out List<HelixWorkItem>? workItemResult))
            {
                List<HelixWorkItem> helixWorkItemsWithConsoleLog = workItemResult
                    .Where(w => !string.IsNullOrEmpty(w.ConsoleLogUrl))
                    .ToList();

                if (helixWorkItemsWithConsoleLog.Count > 0)
                {
                    matchHelixWorkItemWithComments[commentWorkItemKey.Key] = helixWorkItemsWithConsoleLog;
                }
            }
        }

        return matchHelixWorkItemWithComments;
    }
}

public class WorkItemInformation
{
    public string ConsoleLogUrl { get; }
    public int? ExitCode { get; }
    public string? Status { get; }
    public WorkItemInformation(string consoleLogUrl, int? exitCode, string? status = null)
    {
        ConsoleLogUrl = consoleLogUrl;
        ExitCode = exitCode;
        Status = status;
    }
}
