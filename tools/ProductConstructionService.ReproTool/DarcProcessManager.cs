// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
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
                "--bar-uri", Options.Options.PcsLocalUri
            ]);
    }

    public async Task<IAsyncDisposable> AddBuildToChannelAsync(int buildId, string channelName, bool skipCleanup)
    {
        logger.LogInformation("Adding build {build} to channel {channel}", buildId, channelName);
        await ExecuteAsync(["add-build-to-channel", "--id", buildId.ToString(), "--channel", channelName, "--skip-assets-publishing"]);
        return AsyncDisposable.Create(async () =>
        {
            if (skipCleanup)
            {
                return;
            }

            logger.LogInformation("Removing build {buildId} from channel {channelName}", buildId, channelName);
            await ExecuteAsync(["delete-build-from-channel", "--id", buildId.ToString(), "--channel", channelName]);
        });
    }

    public async Task<ProcessExecutionResult> TriggerSubscriptionAsync(string subscriptionId)
    {
        return await ExecuteAsync(
            [
                "trigger-subscriptions",
                "--ids", subscriptionId,
                "-q"
            ]);
    }
}
