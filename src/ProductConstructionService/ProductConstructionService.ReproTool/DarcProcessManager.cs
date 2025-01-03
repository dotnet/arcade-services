// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.RegularExpressions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Core.WebApi.Types;

namespace ProductConstructionService.ReproTool;
internal class DarcProcessManager(
    IProcessManager processManager,
    ILogger<DarcProcessManager> logger)
{
    private string? _darcExePath = null;

    private const string DarcExeName = "darc";

    internal async Task InitializeAsync()
    {
        _darcExePath = await Helpers.Which(DarcExeName);
    }

    internal async Task<ProcessExecutionResult> ExecuteAsync(IEnumerable<string> args)
    {
        if (string.IsNullOrEmpty(_darcExePath))
        {
            throw new InvalidOperationException($"Call {nameof(InitializeAsync)} before trying to execute a darc command");
        }

        return await processManager.Execute(
            _darcExePath,
            [
                .. args,
                "--bar-uri", ReproToolConfiguration.PcsLocalUri
            ]);
    }

    internal async Task<ProcessExecutionResult> DeleteSubscriptionsForChannel(string channelName)
    {
        return await ExecuteAsync(["delete-subscriptions", "--channel", channelName, "--quiet"]);
    }

    internal async Task<IAsyncDisposable> CreateTestChannelAsync(string testChannelName)
    {
        try
        {
            await ExecuteAsync(["delete-channel", "--name", testChannelName]);
        }
        catch (Exception)
        {
            // If there are subscriptions associated to the channel then a previous test clean up failed
            // Run a subscription clean up and try again
            try
            {
                await DeleteSubscriptionsForChannel(testChannelName);
                await ExecuteAsync(["delete-channel", "--name", testChannelName]);
            }
            catch (Exception)
            {
                // Otherwise ignore failures from delete-channel, its just a pre-cleanup that isn't really part of the test
                // And if the test previously succeeded then it'll fail because the channel doesn't exist
            }
        }

        var channel = await ExecuteAsync(["add-channel", "--name", testChannelName, "--classification", "test"]);

        return AsyncDisposable.Create(async () =>
        {
            logger.LogInformation("Cleaning up Test Channel {testChannelName}", testChannelName);
            try
            {
                await ExecuteAsync(["delete-channel", "--name", testChannelName]);
            }
            catch (Exception)
            {
                // Ignore failures from delete-channel on cleanup, this delete is here to ensure that the channel is deleted
                // even if the test does not do an explicit delete as part of the test. Other failures are typical that the channel has already been deleted.
            }
        });
    }

    internal async Task<IAsyncDisposable> AddBuildToChannelAsync(int buildId, string channelName)
    {
        await ExecuteAsync(["add-build-to-channel", "--id", buildId.ToString(), "--channel", channelName, "--skip-assets-publishing"]);
        return AsyncDisposable.Create(async () =>
        {
            logger.LogInformation("Removing build {buildId} from channel {channelName}", buildId, channelName);
            await ExecuteAsync(["delete-build-from-channel", "--id", buildId.ToString(), "--channel", channelName]);
        });
    }

    internal async Task<AsyncDisposableValue<string>> CreateSubscriptionAsync(
        string sourceRepo,
        string targetRepo,
        string channel,
        string targetBranch,
        string? sourceDirectory,
        string? targetDirectory)
    {
        string[] directoryArg = !string.IsNullOrEmpty(sourceDirectory) ?
            ["--source-directory", sourceDirectory] :
            ["--target-directory", targetDirectory!];

        var res = await ExecuteAsync([
                "add-subscription",
                "--channel", channel,
                "--source-repo", sourceRepo,
                "--target-repo", targetRepo,
                "--target-branch", targetBranch,
                "-q",
                "--no-trigger",
                "--source-enabled", "true",
                "--update-frequency", "none",
                .. directoryArg
            ]);

        Match match = Regex.Match(res.StandardOutput, "Successfully created new subscription with id '([a-f0-9-]+)'");
        if (match.Success)
        {
            var subscriptionId = match.Groups[1].Value;
            return AsyncDisposableValue.Create(subscriptionId, async () =>
            {
                logger.LogInformation("Cleaning up Test Subscription {subscriptionId}", subscriptionId);
                try
                {
                    await ExecuteAsync(["delete-subscriptions", "--id", subscriptionId, "--quiet"]);
                }
                catch (Exception)
                {
                    // If this throws an exception the most likely cause is that the subscription was deleted as part of the test case
                }
            });
        }

        throw new Exception("Unable to create subscription.");
    }

    internal async Task<ProcessExecutionResult> TriggerSubscriptionAsync(string subscriptionId)
    {
        return await ExecuteAsync(
            [
                "trigger-subscriptions",
                "--ids", subscriptionId
            ]);
    }
}
