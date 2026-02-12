// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Text;
using System.Text.Json;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.Logging;
using BuildInsights.BuildAnalysis.Models;

namespace BuildInsights.BuildAnalysis;

public interface IHelixDataService
{
    bool IsHelixWorkItem(string comment);
    Task<HelixWorkItem> TryGetHelixWorkItem(string workItemInfo, CancellationToken cancellationToken);
    Task<Dictionary<string, List<HelixWorkItem>>> TryGetHelixWorkItems(ImmutableList<string> workItemInfo, CancellationToken cancellationToken);
}

public class HelixDataProvider : IHelixDataService
{
    private readonly IKustoClientProvider _kustoClientProvider;
    private readonly ILogger<HelixDataProvider> _logger;

    //https://learn.microsoft.com/en-us/azure/data-explorer/kusto/concepts/querylimits#limit-on-query-complexity
    private const int LimitQueryComplexity = 25;

    public HelixDataProvider(IKustoClientProvider kustoClientProvider,
        ILogger<HelixDataProvider> logger)
    {
        _kustoClientProvider = kustoClientProvider;
        _logger = logger;
    }

    public bool IsHelixWorkItem(string comment)
    {
        return TryGetHelixWorkItemFromComment(comment, out HelixWorkItem _);
    }

    public async Task<HelixWorkItem> TryGetHelixWorkItem(string workItemInfo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(workItemInfo)) return null;

        try
        {
            if (!TryGetHelixWorkItemFromComment(workItemInfo, out HelixWorkItem helixWorkItem))
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

    public async Task<Dictionary<string, List<HelixWorkItem>>> TryGetHelixWorkItems(ImmutableList<string> workItemsInfo,
        CancellationToken cancellationToken)
    {
        var helixWorkItem = new Dictionary<string, List<HelixWorkItem>>();
        var relationCommentWorkItemKey = new Dictionary<string, string>();
        foreach (string workItemInfo in workItemsInfo)
        {
            if (TryGetHelixWorkItemFromComment(workItemInfo, out HelixWorkItem workItem))
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
                .Where(t => !string.IsNullOrEmpty(t.ConsoleLogUrl)).ToList();
            helixWorkItems.Add(workItemResult);

            helixWorkItem[keyForHelixWorkItem] = helixWorkItems;
        }

        IEnumerable<HelixWorkItem> helixWorkItemsNotFoundOnKusto = helixWorkItem.Values.SelectMany(h => h.Where(t => t.ConsoleLogUrl == null));
        List<HelixWorkItem> helixWorkItemOnSql = await SqlWorkItemInformation(helixWorkItemsNotFoundOnKusto.ToList(), cancellationToken);
        foreach (HelixWorkItem workItemResult in helixWorkItemOnSql.Where(t => !string.IsNullOrEmpty(t.ConsoleLogUrl)))
        {
            string keyForHelixWorkItem = GetKeyForHelixWorkItem(workItemResult);
            List<HelixWorkItem> helixWorkItems = helixWorkItem.GetValueOrDefault(keyForHelixWorkItem, [])
                .Where(t => !string.IsNullOrEmpty(t.ConsoleLogUrl)).ToList();
            helixWorkItems.Add(workItemResult);

            helixWorkItem[keyForHelixWorkItem] = helixWorkItems;
        }

        return MatchHelixWorkItemsWithComments(helixWorkItem, relationCommentWorkItemKey);
    }

    private async Task<WorkItemInformation> GetHelixWorkItemDetails(string workItemName, string helixJobName,
        CancellationToken cancellationToken)
    {
        return await KustoWorkItemInformation(workItemName, helixJobName) ??
               await SqlWorkItemInformation(workItemName, helixJobName, cancellationToken);
    }

    private async Task<WorkItemInformation> KustoWorkItemInformation(string workItemName, string helixJobName)
    {
        var query = new KustoQuery(@"
 WorkItems
| where FriendlyName == _workItem
| where JobName == _job
| project ExitCode, ConsoleUri, Status");
        query.AddParameter("_workItem", workItemName, KustoDataType.String);
        query.AddParameter("_job", helixJobName, KustoDataType.String);
        IDataReader reader = await _kustoClientProvider.ExecuteKustoQueryAsync(query);

        return !reader.Read() ? null : new WorkItemInformation(reader.GetString(1), reader.GetInt32(0), reader.GetString(2));
    }

    private async Task<WorkItemInformation> SqlWorkItemInformation(string workItemName, string helixJobName,
      CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await GetConnectionAsync(cancellationToken);
        await using SqlCommand command = connection.CreateCommand();
        command.CommandText = @"
SELECT
    EventDataEx.Name,
    EventDataEx.Value
FROM EventDataEx
INNER JOIN EventsEx ON EventsEx.EventId = EventDataEx.EventId
INNER JOIN WorkItems W on EventsEx.WorkItemId = W.WorkItemId
INNER JOIN Jobs J on W.JobId = J.JobId
	AND J.Name = @jobName
    AND W.FriendlyName = @workitemName
	AND EventsEx.Type = 'WorkItemFinished'
	AND (EventDataEx.Name = 'LogUri' OR EventDataEx.Name = 'ExitCode')";
        command.Parameters.Add("jobName", SqlDbType.VarChar, 200).Value = helixJobName;
        command.Parameters.Add("workitemName", SqlDbType.NVarChar, 200).Value = workItemName;

        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string consoleLogValue = string.Empty;
        string exitCodeValue = string.Empty;
        do
        {
            switch (reader.GetString(0).TrimEnd())
            {
                case "LogUri":
                    consoleLogValue = reader.GetString(1);
                    break;
                case "ExitCode":
                    exitCodeValue = reader.GetString(1);
                    break;
            }
        } while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false));

        int? exitCode = null;
        if (!string.IsNullOrEmpty(exitCodeValue) && int.TryParse(exitCodeValue, out int parsedExitCode))
        {
            exitCode = parsedExitCode;
        }

        return new WorkItemInformation(consoleLogValue, exitCode);
    }

    public async Task<List<HelixWorkItem>> KustoWorkItemInformation(List<HelixWorkItem> helixWorkItems)
    {
        if (helixWorkItems.Count == 0) return [];

        var workItemInformation = new List<HelixWorkItem>();
        for (int i = 0; i < Math.Ceiling(helixWorkItems.Count/(double)LimitQueryComplexity); i++)
        {
            List<HelixWorkItem> recordsToQuery = helixWorkItems.Skip(LimitQueryComplexity * i).Take(LimitQueryComplexity).ToList();
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

    private KustoQuery CreateKustoQueryForHelixWorkItems(List<HelixWorkItem> helixWorkItems)
    {
        var query = new KustoQuery();
        var queryText = new StringBuilder();
        queryText.AppendLine("WorkItems");

        int count = 0;
        foreach (HelixWorkItem workItem in helixWorkItems)
        {
            queryText.AppendLine(count == 0
                ? @$"| where FriendlyName == _workItem{count} and JobName == _job{count}"
                : @$"or FriendlyName == _workItem{count} and JobName == _job{count}");

            query.AddParameter($"_workItem{count}", workItem.HelixWorkItemName, KustoDataType.String);
            query.AddParameter($"_job{count}", workItem.HelixJobId, KustoDataType.String);

            count++;
        }

        queryText.AppendLine("| project JobName, FriendlyName, ExitCode, ConsoleUri");
        query.Text = queryText.ToString();
        return query;
    }

    private async Task<List<HelixWorkItem>> SqlWorkItemInformation(List<HelixWorkItem> helixWorkItems,
      CancellationToken cancellationToken)
    {
        if (helixWorkItems.Count == 0) return [];

        List<WorkItemSqlResult> sqlQueryResultForWorkItems = [];
        for (int i = 0; i < Math.Ceiling(helixWorkItems.Count / (double)LimitQueryComplexity); i++)
        {
            List<HelixWorkItem> recordsToQuery = helixWorkItems.Skip(LimitQueryComplexity * i).Take(LimitQueryComplexity).ToList();
            sqlQueryResultForWorkItems.AddRange(await GetSqlQueryResultsForWorkItems(recordsToQuery, cancellationToken));
        }

        sqlQueryResultForWorkItems = await GetSqlQueryResultsForWorkItems(helixWorkItems, cancellationToken);

        foreach (HelixWorkItem helixWorkItem in helixWorkItems)
        {
            List<WorkItemSqlResult> workItemQueryResult = sqlQueryResultForWorkItems.Where(t =>
                t.JobName.Equals(helixWorkItem.HelixJobId)
                && t.WorkItemName.Equals(helixWorkItem.HelixWorkItemName)).ToList();

            string consoleLogValue = string.Empty;
            string exitCodeValue = string.Empty;

            foreach (WorkItemSqlResult result in workItemQueryResult)
            {
                switch (result.EventName)
                {
                    case "LogUri":
                        consoleLogValue = result.EventValue;
                        break;
                    case "ExitCode":
                        exitCodeValue = result.EventValue;
                        break;
                }
            }

            int? exitCode = null;
            if (!string.IsNullOrEmpty(exitCodeValue) && int.TryParse(exitCodeValue, out int parsedExitCode))
            {
                exitCode = parsedExitCode;
            }

            helixWorkItem.ConsoleLogUrl = consoleLogValue;
            helixWorkItem.ExitCode = exitCode;
        }

        return helixWorkItems;
    }

    private async Task<List<WorkItemSqlResult>> GetSqlQueryResultsForWorkItems(List<HelixWorkItem> helixWorkItems,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await GetConnectionAsync(cancellationToken);
        await using SqlCommand command = connection.CreateCommand();
        var queryText = new StringBuilder();
        queryText.Append(@"
SELECT
J.Name,
W.FriendlyName,
EventDataEx.Name,
EventDataEx.Value
FROM EventDataEx
INNER JOIN EventsEx ON EventsEx.EventId = EventDataEx.EventId
INNER JOIN WorkItems W on EventsEx.WorkItemId = W.WorkItemId
INNER JOIN Jobs J on W.JobId = J.JobId
AND EventsEx.Type = 'WorkItemFinished'
AND (EventDataEx.Name = 'LogUri' OR EventDataEx.Name = 'ExitCode')
");

        int count = 0;
        foreach (HelixWorkItem workItem in helixWorkItems)
        {
            queryText.AppendLine(count == 0
                ? @$" AND (J.Name = @jobName{count} AND W.FriendlyName = @workitemName{count}"
                : @$" OR J.Name =  @jobName{count}  AND W.FriendlyName = @workitemName{count}");

            command.Parameters.Add($"jobName{count}", SqlDbType.VarChar, 200).Value = workItem.HelixJobId;
            command.Parameters.Add($"workitemName{count}", SqlDbType.NVarChar, 200).Value =
                workItem.HelixWorkItemName;

            count++;
        }
        queryText.AppendLine(")");
        command.CommandText = queryText.ToString();

        var sqlQueryResult = new List<WorkItemSqlResult>();
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sqlQueryResult.Add(new WorkItemSqlResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2).TrimEnd(),
                reader.GetString(3)
            ));
        }

        return sqlQueryResult;
    }

    private static bool TryGetHelixWorkItemFromComment(string comment, out HelixWorkItem helixWorkItem)
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

    private Dictionary<string, List<HelixWorkItem>> MatchHelixWorkItemsWithComments(
        IReadOnlyDictionary<string, List<HelixWorkItem>> helixWorkItems,
        IReadOnlyDictionary<string, string> relationCommentWorkItemKey)
    {
        var matchHelixWorkItemWithComments = new Dictionary<string, List<HelixWorkItem>>();

        foreach (KeyValuePair<string, string> commentWorkItemKey in relationCommentWorkItemKey)
        {
            if (helixWorkItems.TryGetValue(commentWorkItemKey.Value, out List<HelixWorkItem> workItemResult))
            {
                List<HelixWorkItem> helixWorkItemsWithConsoleLog = workItemResult.Where(w => !string.IsNullOrEmpty(w.ConsoleLogUrl)).ToList();

                if (helixWorkItemsWithConsoleLog.Count > 0)
                {
                    matchHelixWorkItemWithComments[commentWorkItemKey.Key] = helixWorkItemsWithConsoleLog;
                }
            }
        }
        
        return matchHelixWorkItemWithComments;
    }

    private class WorkItemSqlResult
    {
        public string JobName { get; }
        public string WorkItemName { get; }
        public string EventName { get; }
        public string EventValue { get; }
        public WorkItemSqlResult(string jobName, string workItemName, string eventName, string eventValue)
        {
            JobName = jobName;
            WorkItemName = workItemName;
            EventName = eventName;
            EventValue = eventValue;
        }
    }
}

public class WorkItemInformation
{
    public string ConsoleLogUrl { get; }
    public int? ExitCode { get; }
    public string Status { get; }
    public WorkItemInformation(string consoleLogUrl, int? exitCode, string status = null)
    {
        ConsoleLogUrl = consoleLogUrl;
        ExitCode = exitCode;
        Status = status;
    }
}
