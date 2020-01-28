using System;
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

            void ProcessDataReceived(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null)
                {
                    output.AppendLine(e.Data);
                    testOutput.WriteLine(e.Data);
                }
            }

            var psi = new ProcessStartInfo(executable)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process
            {
                StartInfo = psi
            };
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.OutputDataReceived += ProcessDataReceived;
            process.ErrorDataReceived += ProcessDataReceived;
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) => { tcs.TrySetResult(true); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await tcs.Task.ConfigureAwait(false);

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
                var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd";
                return (await RunExecutableAsync(testOutput, cmd, "/c", $"where {command}")).Trim();
            }

            return (await RunExecutableAsync(testOutput, "/bin/sh", "-c", $"which {command}")).Trim();
        }
    }
}
