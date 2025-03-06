// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

namespace FlatFlowMigrationCli;

/// <summary>
/// Class that logs the operations in console and into a file instead of performing them.
/// </summary>
internal class MigrationLogger : ISubscriptionMigrator
{
    private readonly ILogger<MigrationLogger> _logger;

    public MigrationLogger(ILogger<MigrationLogger> logger)
    {
        _logger = logger;
    }

    public async Task DisableSubscriptionAsync(Subscription subscription)
    {
        _logger.LogInformation("Would disable a subscription {subscriptionId} {sourceRepository} -> {targetRepository}",
            subscription.Id,
            subscription.SourceRepository,
            subscription.TargetRepository);

        await LogActionAsync(GetActionKey(subscription), Action.Disable, subscription.Id.ToString());
    }

    public async Task DeleteSubscriptionAsync(Subscription subscription)
    {
        _logger.LogInformation("Would delete an existing subscription {subscriptionId}...", subscription.Id);
        await LogActionAsync(GetActionKey(subscription), Action.Delete, subscription.Id.ToString());
    }

    public async Task CreateVmrSubscriptionAsync(Subscription subscription)
    {
        _logger.LogInformation("Would create subscription VMR -> {repoUri}", subscription.TargetRepository);
        await LogActionAsync($"VMR -> {subscription.TargetRepository}", Action.Create, "new");
    }

    public async Task CreateBackflowSubscriptionAsync(string mappingName, string repoUri, string branch, HashSet<string> excludedAssets)
    {
        _logger.LogInformation("Would create a backflow subscription for {repoUri}", repoUri);
        await LogActionAsync($"VMR -> {repoUri} (codeflow)", Action.Create, "new");
    }

    private async Task LogActionAsync(string repoUri, Action action, string id, Dictionary<string, string>? Parameters = null)
    {
        var log = await ReadLog();

        if (!log.TryGetValue(repoUri, out var repoActions))
        {
            repoActions = new List<RepoActionLog>();
            log.Add(repoUri, repoActions);
        }

        repoActions.Add(new RepoActionLog(action, id, Parameters));

        await WriteLog(log);
    }

    private async Task<ActionLog> ReadLog()
    {
        if (!File.Exists("migration.log"))
        {
            return new ActionLog();
        }

        using var file = File.Open("migration.log", FileMode.Open);

        try
        {
            var log = await JsonSerializer.DeserializeAsync<ActionLog>(file, SerializerOptions);
            file.Close();
            return log ?? new ActionLog();
        }
        catch
        {
            file.Close();
            return new ActionLog();
        }
    }

    private async Task WriteLog(ActionLog log)
    {
        using var file = File.Open("migration.log", FileMode.Create);
        await JsonSerializer.SerializeAsync(file, log, SerializerOptions);
        file.Close();
    }

    private static string GetActionKey(Subscription subscription)
        => $"{subscription.SourceRepository} - {subscription.TargetRepository}";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal class ActionLog : Dictionary<string, List<RepoActionLog>>
{}
internal record RepoActionLog(Action Action, string Id, Dictionary<string, string>? Parameters);
internal enum Action
{
    Unknown,
    Create,
    Disable,
    Delete,
}
