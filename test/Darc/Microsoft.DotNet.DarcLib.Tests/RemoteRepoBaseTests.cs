// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.Internal.Credentials;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Microsoft.DotNet.DarcLib.Tests;

[TestFixture]
public class RemoteRepoBaseTests
{
    private const string FilePath = "eng/common/test-script.sh";

    [TestCase(null, "100755", TestName = "CommitFilesAsync_NewExecutableFile_RecordsExecutableMode")]
    [TestCase("100644", "100755", TestName = "CommitFilesAsync_NonExecutableFile_RecordsExecutableMode")]
    [TestCase("100755", "100644", TestName = "CommitFilesAsync_ExecutableFile_RecordsNonExecutableMode")]
    public async Task CommitFilesAsync_RequestedMode_RecordsRequestedMode(
        string? initialMode,
        string requestedMode)
    {
        // Arrange
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"remote-repo-base-tests-{Guid.NewGuid():N}");
        string sourceRepository = Path.Combine(temporaryDirectory, "source");
        string remoteRepository = Path.Combine(temporaryDirectory, "remote.git");
        Directory.CreateDirectory(sourceRepository);

        var processManager = new ProcessManager(NullLogger<ProcessManager>.Instance, "git");

        try
        {
            await ExecuteGitAsync(processManager, sourceRepository, "init", "-b", "main");
            await ExecuteGitAsync(processManager, sourceRepository, "config", "user.email", "test@example.com");
            await ExecuteGitAsync(processManager, sourceRepository, "config", "user.name", "Test");
            await ExecuteGitAsync(processManager, sourceRepository, "config", "commit.gpgsign", "false");

            string readmePath = Path.Combine(sourceRepository, "README.md");
            await File.WriteAllTextAsync(readmePath, "test\n");
            await ExecuteGitAsync(processManager, sourceRepository, "add", "README.md");

            if (initialMode != null)
            {
                string scriptPath = Path.Combine(sourceRepository, FilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
                await File.WriteAllTextAsync(scriptPath, "#!/usr/bin/env bash\n");
                await ExecuteGitAsync(processManager, sourceRepository, "add", FilePath);
                await ExecuteGitAsync(
                    processManager,
                    sourceRepository,
                    "update-index",
                    initialMode == "100755" ? "--chmod=+x" : "--chmod=-x",
                    "--",
                    FilePath);
            }

            await ExecuteGitAsync(processManager, sourceRepository, "commit", "-m", "Initial commit");
            await ExecuteGitAsync(processManager, temporaryDirectory, "clone", "--bare", sourceRepository, remoteRepository);

            using var cache = new MemoryCache(new MemoryCacheOptions());
            var client = new TestRemoteRepoBase(
                processManager,
                temporaryDirectory,
                cache);
            var file = new GitFile(
                FilePath,
                "#!/usr/bin/env bash\necho test\n",
                ContentEncoding.Utf8,
                requestedMode);

            // Act
            await client.CommitFilesAsync([file], remoteRepository, "main", "Update script");

            // Assert
            var result = await processManager.Execute(
                "git",
                [$"--git-dir={remoteRepository}", "ls-tree", "main", "--", FilePath],
                workingDir: temporaryDirectory);
            result.ThrowIfFailed("git ls-tree failed");
            result.StandardOutput.Should().StartWith($"{requestedMode} blob ");
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                DependencyFileManager.NormalizeAttributes(temporaryDirectory);
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static async Task ExecuteGitAsync(
        IProcessManager processManager,
        string repository,
        params string[] arguments)
    {
        var result = await processManager.ExecuteGit(repository, arguments);
        result.ThrowIfFailed($"git {string.Join(' ', arguments)} failed");
    }

    private sealed class TestRemoteRepoBase(
        IProcessManager processManager,
        string temporaryRepositoryPath,
        IMemoryCache cache)
        : RemoteRepoBase(
            new ResolvedTokenProvider("token"),
            processManager,
            temporaryRepositoryPath,
            cache,
            NullLogger.Instance)
    {
        public Task CommitFilesAsync(
            List<GitFile> filesToCommit,
            string repoUri,
            string branch,
            string commitMessage)
            => base.CommitFilesAsync(
                filesToCommit,
                repoUri,
                branch,
                commitMessage,
                NullLogger.Instance,
                "token",
                "Test",
                "test@example.com");
    }
}
