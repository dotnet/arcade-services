// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;

namespace ProductConstructionService.ScenarioTests.Helpers;

public static class TestHelpers
{
    public static async Task<string> RunExecutableAsync(string executable, params string[] args)
    {
        return await RunExecutableAsyncWithInput(executable, "", args);
    }

    public static async Task<string> RunExecutableAsyncWithInput(string executable, string input, params string[] args)
    {
        TestContext.WriteLine(FormatExecutableCall(executable, args));
        var output = new StringBuilder();

        void WriteOutput(string message)
        {
            if (message != null)
            {
                Debug.WriteLine(message);
                output.AppendLine(message);
                TestContext.WriteLine(message);
            }
        }

        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            // The string append used by Process will accept null params 
            // and then throw without identifying the param, so we need to handle it here to get logging
            if (arg != null)
            {
                psi.ArgumentList.Add(arg);
            }
            else
            {
                WriteOutput("Null parameter encountered while constructing command string.");
            }
        }

        using var process = new Process
        {
            StartInfo = psi
        };

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.EnableRaisingEvents = true;
        process.Exited += (s, e) => { tcs.TrySetResult(true); };
        process.Start();

        Task<bool> exitTask = tcs.Task;
        var stdin = Task.Run(() => { process.StandardInput.Write(input); process.StandardInput.Close(); });
        Task<string> stdout = process.StandardOutput.ReadLineAsync();
        Task<string> stderr = process.StandardError.ReadLineAsync();
        var list = new List<Task> { exitTask, stdout, stderr, stdin };
        while (list.Count != 0)
        {
            var done = await Task.WhenAny(list);
            list.Remove(done);
            if (done == exitTask)
            {
                continue;
            }

            if (done == stdout)
            {
                var data = await stdout;
                WriteOutput(data);
                if (data != null)
                {
                    list.Add(stdout = process.StandardOutput.ReadLineAsync());
                }
                continue;
            }

            if (done == stderr)
            {
                var data = await stderr;
                WriteOutput(data);
                if (data != null)
                {
                    list.Add(stderr = process.StandardError.ReadLineAsync());
                }
                continue;
            }

            if (done == stdin)
            {
                await stdin;
                continue;
            }

            throw new InvalidOperationException("Unexpected Task completed.");
        }

        if (process.ExitCode != 0)
        {
            var exceptionWithConsoleLog = new ScenarioTestException($"{executable} exited with code {process.ExitCode}");
            exceptionWithConsoleLog.Data.Add("ConsoleOutput", output.ToString());
            throw exceptionWithConsoleLog;
        }

        return output.ToString();
    }

    public static async Task<string> Which(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd";
            return (await RunExecutableAsync(cmd, "/c", $"where {command}")).Trim()
                   // get the first line of where's output
                   .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }

        return (await RunExecutableAsync("/bin/sh", "-c", $"which {command}")).Trim();
    }

    public static string FormatExecutableCall(string executable, params string[] args)
    {
        var output = new StringBuilder();
        var secretArgNames = new[] { "-p", "--password", "--github-pat", "--azdev-pat" };

        output.Append(executable);
        for (var i = 0; i < args.Length; i++)
        {
            output.Append(' ');

            if (i > 0 && secretArgNames.Contains(args[i - 1]))
            {
                output.Append("\"***\"");
                continue;
            }

            output.Append($"\"{args[i]}\"");
        }

        return output.ToString();
    }
}

public class ScenarioTestException : Exception
{
    public ScenarioTestException(string message)
    {
        TestContext.WriteLine(message);
    }
}
