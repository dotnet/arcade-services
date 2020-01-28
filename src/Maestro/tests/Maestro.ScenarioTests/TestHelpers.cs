using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Maestro.ScenarioTests
{
    public static class TestHelpers
    {
        public static async Task<string> RunExecutableAsync(ITestOutputHelper testOutput, string executable, params string[] args)
        {
            testOutput.WriteLine($"{executable} {string.Join(" ", args.Select(a => $"\"{a}\""))}");
            var output = new StringBuilder();

            void WriteOutput(string message)
            {
                if (message != null)
                {
                    Debug.WriteLine(message);
                    output.AppendLine(message);
                    testOutput.WriteLine(message);
                }
            }

            var psi = new ProcessStartInfo(executable)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
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
            Task<string> stdout = process.StandardOutput.ReadLineAsync();
            Task<string> stderr = process.StandardError.ReadLineAsync();
            var list = new List<Task> {exitTask, stdout, stderr};
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

                throw new InvalidOperationException("Unexpected Task completed.");
            }



            if (process.ExitCode != 0)
            {
                throw new XunitException($"{executable} exited with code {process.ExitCode}");
            }

            return output.ToString();
        }

        public static async Task<string> Which(ITestOutputHelper testOutput, string command)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd";
                return (await RunExecutableAsync(testOutput, cmd, "/c", $"where {command}")).Trim();
            }

            return (await RunExecutableAsync(testOutput, "/bin/sh", "-c", $"which {command}")).Trim();
        }
    }
}
