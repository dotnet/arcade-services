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

    Task<ProcessExecutionResult> ExecuteGit(
        string repoPath,
        string[] arguments,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default);

    Task<ProcessExecutionResult> ExecuteGit(string repoPath, params string[] arguments)
        => ExecuteGit(repoPath, arguments.ToArray(), null, default);

    Task<ProcessExecutionResult> ExecuteGit(
        string repoPath,
        IEnumerable<string> arguments,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default)
        => ExecuteGit(repoPath, arguments.ToArray(), envVariables, cancellationToken);

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

    public Task<ProcessExecutionResult> ExecuteGit(
        string repoPath,
        string[] arguments,
        Dictionary<string, string>? envVariables = null,
        CancellationToken cancellationToken = default)
        => Execute(GitExecutable, (new[] { "-C", repoPath }).Concat(arguments), envVariables: envVariables, cancellationToken: cancellationToken);

    public async Task<ProcessExecutionResult> Execute(
        string executable,
        IEnumerable<string> arguments,
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
    /// Traverses the directory structure from given path up until it finds a .git folder.
    /// </summary>
    /// <param name="path">tarting directory</param>
    /// <returns>A root of a git repository (throws when no .git found)</returns>
    public string FindGitRoot(string path)
    {
        var dir = new DirectoryInfo(path);
        while (!Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;

            if (dir == null)
            {
                throw new Exception($"Failed to find parent git repository for {path}");
            }
        }

        return dir.FullName;
    }
}
