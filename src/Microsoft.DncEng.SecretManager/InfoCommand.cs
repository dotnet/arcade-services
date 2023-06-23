using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager;

[Command("info")]
class InfoCommand : Command
{
    private readonly IConsole _console;

    public InfoCommand(IConsole console)
    {
        _console = console;
    }

    public override Task RunAsync(CancellationToken cancellationToken)
    {
        var exeName = Process.GetCurrentProcess().ProcessName;
        var version = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "-.-.-.-";
        _console.WriteImportant($"{exeName} version {version}");
        return Task.CompletedTask;
    }
}
