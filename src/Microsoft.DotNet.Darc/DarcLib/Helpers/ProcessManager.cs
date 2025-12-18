// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.DotNet.Services.Utility;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Helpers;

public interface IProcessManager
{
    Task<ProcessExecutionResult> Execute(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan? timeout = null,
        string? workingDir = null,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> Execute(
        string executable,
        IEnumerable<string> arguments,
        string? standardInput,
        TimeSpan? timeout = null,
        string? workingDir = null,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> ExecuteGit(
        string repoPath,
        string[] arguments,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> ExecuteGit(
        string repoPath,
        string[] arguments,
        string? standardInput,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> ExecuteGit(string repoPath, params string[] arguments)
        => ExecuteGit(repoPath, [.. arguments], null, default);

    Task<ProcessExecutionResult> ExecuteGit(
        string repoPath,
        IEnumerable<string> arguments,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default)
        => ExecuteGit(repoPath, [.. arguments], envVariables, cancellationToken);

    Task<ProcessExecutionResult> ExecuteGit(
        string repoPath,
        IEnumerable<string> arguments,
        string? standardInput,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default)
        => ExecuteGit(repoPath, [.. arguments], standardInput, envVariables, cancellationToken);

    string FindGitRoot(string path);

    string GitExecutable { get; }
}

public class ProcessManager : IProcessManager
{
    private readonly ILogger _logger;

    public string GitExecutable { get; }

    public ProcessManager(ILogger logger, string gitExecutable)
    {
        _logger = logger;
        GitExecutable = gitExecutable;
    }

    public async Task<ProcessExecutionResult> ExecuteGit(
        string repoPath,
        string[] arguments,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteGit(
            repoPath,
            arguments,
            standardInput: null,
            envVariables: envVariables,
            cancellationToken: cancellationToken);
    }

    public async Task<ProcessExecutionResult> ExecuteGit(
        string repoPath,
        string[] arguments,
        string? standardInput,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default)
    {
        // When another process is using the directory, we retry a few times
        return await ExponentialRetry.Default.RetryAsync(
            async () => await Execute(
                GitExecutable,
                (new[] { "-C", repoPath }).Concat(arguments),
                standardInput,
                envVariables: envVariables,
                cancellationToken: cancellationToken),
            ex => _logger.LogDebug("Another git process seems to be running in this repository, retrying..."),
            ex => ex is ProcessFailedException e && e.ExecutionResult.ExitCode == 128 && e.ExecutionResult.StandardError.Contains(".git/index.lock"));
    }

    public Task<ProcessExecutionResult> Execute(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan? timeout = null,
        string? workingDir = null,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default)
        => Execute(
            executable,
            arguments,
            standardInput: null,
            timeout,
            workingDir,
            envVariables,
            cancellationToken);

    public async Task<ProcessExecutionResult> Execute(
        string executable,
        IEnumerable<string> arguments,
        string? standardInput,
        TimeSpan? timeout = null,
        string? workingDir = null,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default)
    {
        var processStartInfo = new ProcessStartInfo()
        {
            FileName = executable,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput != null,
            WorkingDirectory = workingDir,
        };

        foreach (var arg in arguments)
        {
            processStartInfo.ArgumentList.Add(arg);
        }

        var logMessage = $"Executing command: '{executable} "
            + string.Join(' ', processStartInfo.ArgumentList) + '\'';

        if (workingDir != null)
        {
            logMessage = $"{logMessage} in {workingDir}";
        }

        if (envVariables != null)
        {
            foreach (var envVar in envVariables)
            {
                processStartInfo.Environment[envVar.Key] = envVar.Value;
            }
        }

        _logger.LogDebug(logMessage);

        var p = new Process() { StartInfo = processStartInfo };

        var standardOut = new StringBuilder();
        var standardErr = new StringBuilder();

        p.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
        {
            lock (standardOut)
            {
                if (e.Data != null)
                {
                    standardOut.AppendLine(e.Data);
                }
            }
        };

        p.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
        {
            lock (standardErr)
            {
                if (e.Data != null)
                {
                    standardErr.AppendLine(e.Data);
                }
            }
        };

        p.Start();

        if (standardInput != null)
        {
            await p.StandardInput.WriteAsync(standardInput);
            await p.StandardInput.FlushAsync();
            p.StandardInput.Close();
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        bool timedOut = false;
        int exitCode;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (timeout.HasValue)
        {
            cts.CancelAfter((int) Math.Min(timeout.Value.TotalMilliseconds, int.MaxValue));
        }

        await p.WaitForExitAsync(cts.Token);

        if (cts.IsCancellationRequested)
        {
            _logger.LogError("Waiting for command timed out");
            timedOut = true;
            exitCode = -2;

            // try to terminate the process
            try { p.Kill(); } catch { }
        }
        else
        {
            // we exited normally, call WaitForExit() again to ensure redirected standard output is processed
            await p.WaitForExitAsync();
            exitCode = p.ExitCode;
        }

        p.Close();

        lock (standardOut)
        lock (standardErr)
        {
            return new ProcessExecutionResult()
            {
                ExitCode = exitCode,
                StandardOutput = standardOut.ToString(),
                StandardError = standardErr.ToString(),
                TimedOut = timedOut
            };
        }
    }

    /// <summary>
    /// Traverses the directory structure from given path up until it finds a git repository root.
    /// </summary>
    /// <param name="path">Starting directory</param>
    /// <returns>A root of a git repository (throws when no .git found)</returns>
    public string FindGitRoot(string path)
    {
        var dir = new DirectoryInfo(path);
        while (!HasGitRepository(dir.FullName))
        {
            dir = dir.Parent;

            if (dir == null)
            {
                throw new Exception($"Failed to find parent git repository for {path}");
            }
        }

        return dir.FullName;
    }

    private static bool HasGitRepository(string directoryPath)
    {
        var gitPath = Path.Combine(directoryPath, ".git");
        
        // Check for .git directory (regular repository)
        if (Directory.Exists(gitPath))
        {
            return true;
        }
        
        // Check for .git file (worktree)
        if (File.Exists(gitPath))
        {
            return true;
        }
        
        return false;
    }
}
