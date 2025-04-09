// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Tools.Common;

namespace FlatFlowMigrationCli;

/// <summary>
/// Class that logs the operations in console and into a file instead of performing them.
/// </summary>
internal class MigrationLogger : ISubscriptionMigrator
{
    private readonly ILogger<MigrationLogger> _logger;
    private readonly string _outputPath;

    public MigrationLogger(ILogger<MigrationLogger> logger, string outputPath)
    {
        _logger = logger;
        _outputPath = outputPath;
    }

    public async Task DisableSubscriptionAsync(Subscription subscription)
    {
        _logger.LogDebug("Would disable a subscription {sourceRepository} -> {targetRepository} / {subscriptionId}",
            RemoveUrlPrefix(subscription.SourceRepository),
            RemoveUrlPrefix(subscription.TargetRepository),
            subscription.Id);

        await LogActionAsync(GetActionKey(subscription), Action.Disable, subscription.Id.ToString());
    }

    public async Task DeleteSubscriptionAsync(Subscription subscription)
    {
        _logger.LogDebug("Would delete an existing subscription {sourceRepository} -> {targetRepository} / {subscriptionId}...",
            RemoveUrlPrefix(subscription.SourceRepository),
            RemoveUrlPrefix(subscription.TargetRepository),
            subscription.Id);
        await LogActionAsync(GetActionKey(subscription), Action.Delete, subscription.Id.ToString());
    }

    public async Task CreateVmrSubscriptionAsync(Subscription subscription)
    {
        _logger.LogDebug("Would create subscription {vmrUri} -> {repoUri}",
            RemoveUrlPrefix(Constants.VmrUri),
            RemoveUrlPrefix(subscription.TargetRepository));
        await LogActionAsync($"{Constants.VmrUri} - {subscription.TargetRepository}", Action.Create, null, new()
        {
            { "codeflow", false },
            { "branch", subscription.TargetBranch },
        });
    }

    public async Task CreateBackflowSubscriptionAsync(string mappingName, string repoUri, string branch, HashSet<string> excludedAssets)
    {
        _logger.LogDebug("Would create a backflow subscription for {repoUri}", RemoveUrlPrefix(repoUri));
        await LogActionAsync($"{Constants.VmrUri} - {repoUri}", Action.Create, null, new()
        {
            { "codeflow", true },
            { "branch", branch },
            { "excludedAssets", string.Join(", ", excludedAssets) },
        });
    }

    public async Task CreateForwardFlowSubscriptionAsync(string mappingName, string repoUri, string channelName)
    {
        _logger.LogDebug("Would create a forward flow subscription for {repoUri}", RemoveUrlPrefix(repoUri));
        await LogActionAsync($"{repoUri} - VMR", Action.Create, null, new()
        {
            { "codeflow", true },
            { "channel", channelName },
        });
    }

    private async Task LogActionAsync(string repoUri, Action action, string? id, Dictionary<string, object?>? Parameters = null)
    {
        var log = await ReadLogAsync(_outputPath);
        log[repoUri] = new RepoActionLog(action, id, Parameters);
        await WriteLog(log);
    }

    public static async Task<ActionLog> ReadLogAsync(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        using var file = File.Open(path, FileMode.Open);

        try
        {
            var log = await JsonSerializer.DeserializeAsync<ActionLog>(file, SerializerOptions);
            file.Close();
            return log ?? [];
        }
        catch
        {
            file.Close();
            return [];
        }
    }

    private async Task WriteLog(ActionLog log)
    {
        using var file = File.Open(_outputPath, FileMode.Create);
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

    private static string RemoveUrlPrefix(string url) => url.Replace("https://github.com/", null);
}

internal class ActionLog : Dictionary<string, RepoActionLog>{}

internal record RepoActionLog(Action Action, string? Id, Dictionary<string, object?>? Parameters);

internal enum Action
{
    Unknown,
    Create,
    Disable,
    Delete,
}
