// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.ServiceModel.Channels;
using System.Text.RegularExpressions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging;

namespace ProductConstructionService.ReproTool;

internal class DarcProcessManager(
    IProcessManager processManager,
    ILogger<DarcProcessManager> logger)
{
    private string? _darcExePath = null;

    private const string DarcExeName = "darc";

    public async Task InitializeAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd";
            _darcExePath = (await processManager.Execute(
                cmd,
                [
                    "/c",
                    "where",
                    DarcExeName
                ])).StandardOutput.Trim();
        }
        else
        {
            _darcExePath = (await processManager.Execute(
            "/bin/sh",
            [
                "-c",
                "which",
                DarcExeName
            ])).StandardOutput.Trim();
        }
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

    public async Task<ProcessExecutionResult> DeleteSubscriptionsForChannelAsync(string channelName)
    {
        return await ExecuteAsync(["delete-subscriptions", "--channel", channelName, "--quiet"]);
    }

    public async Task<ProcessExecutionResult> DeleteChannelAsync(string channelName)
    {
        return await ExecuteAsync(["delete-channel", "--name", channelName]);
    }

    public async Task<IAsyncDisposable> CreateTestChannelAsync(string testChannelName)
    {
        logger.LogInformation("Creating test channel {channelName}", testChannelName);

        try
        {
            await DeleteChannelAsync(testChannelName);
        }
        catch (Exception)
        {
            // If there are subscriptions associated to the channel then a previous test clean up failed
            // Run a subscription clean up and try again
            try
            {
                await DeleteSubscriptionsForChannelAsync(testChannelName);
                await DeleteChannelAsync(testChannelName);
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
                await DeleteChannelAsync(testChannelName);
            }
            catch (Exception)
            {
                // Ignore failures from delete-channel on cleanup, this delete is here to ensure that the channel is deleted
                // even if the test does not do an explicit delete as part of the test. Other failures are typical that the channel has already been deleted.
            }
        });
    }

    public async Task<IAsyncDisposable> AddBuildToChannelAsync(int buildId, string channelName)
    {
        logger.LogInformation("Adding build {build} to channel {channel}", buildId, channelName);
        await ExecuteAsync(["add-build-to-channel", "--id", buildId.ToString(), "--channel", channelName, "--skip-assets-publishing"]);
        return AsyncDisposable.Create(async () =>
        {
            logger.LogInformation("Removing build {buildId} from channel {channelName}", buildId, channelName);
            await ExecuteAsync(["delete-build-from-channel", "--id", buildId.ToString(), "--channel", channelName]);
        });
    }

    public async Task<AsyncDisposableValue<string>> CreateSubscriptionAsync(
        string sourceRepo,
        string targetRepo,
        string channel,
        string targetBranch,
        string? sourceDirectory,
        string? targetDirectory)
    {
        logger.LogInformation("Creating a test subscription");

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

    public async Task<ProcessExecutionResult> TriggerSubscriptionAsync(string subscriptionId)
    {
        return await ExecuteAsync(
            [
                "trigger-subscriptions",
                "--ids", subscriptionId
            ]);
    }
}
