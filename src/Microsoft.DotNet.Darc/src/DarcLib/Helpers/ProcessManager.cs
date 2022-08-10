// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Microsoft.DotNet.DarcLib.Helpers;

public interface IProcessManager
{
    Task<ProcessExecutionResult> Execute(string executable, IEnumerable<string> arguments, TimeSpan? timeout = null);

    Task<ProcessExecutionResult> ExecuteGit(string repoPath, params string[] arguments);

    Task<ProcessExecutionResult> ExecuteGit(string repoPath, IEnumerable<string> arguments)
        => ExecuteGit(repoPath, arguments.ToArray());

    string FindGitRoot(string path);

    string GitExecutable { get; }
}

public class ProcessManager : IProcessManager
{
    private readonly ILogger _logger;

    public string GitExecutable { get; }

    public ProcessManager(ILogger<IProcessManager> logger, string gitExecutable)
    {
        _logger = logger;
        GitExecutable = gitExecutable;
    }

    public Task<ProcessExecutionResult> ExecuteGit(string repoPath, params string[] arguments)
        => Execute(GitExecutable, (new[] { "-C", repoPath }).Concat(arguments));

    public async Task<ProcessExecutionResult> Execute(string executable, IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var processStartInfo = new ProcessStartInfo()
        {
            FileName = executable,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in arguments)
        {
            processStartInfo.ArgumentList.Add(arg);
        }

        _logger.LogDebug("Executing command: '{executable} {arguments}'",
            executable, StringUtils.FormatArguments(processStartInfo.ArgumentList));

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
        var cts = new CancellationTokenSource();

        if (timeout.HasValue)
        {
            cts.CancelAfter((int) Math.Min(timeout.Value.TotalMilliseconds, int.MaxValue));
            await p.WaitForExitAsync(cts.Token);
        }
        else
        {
            await p.WaitForExitAsync();
        }

        if (cts.IsCancellationRequested)
        {
            _logger.LogError("Waiting for command timed out: execution may be compromised");
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
