using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.CommandLineLib.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DncEng.SecretManager
{
    public class Program : DependencyInjectedConsoleApp
    {
        public static Task<int> Main(string[] args)
        {
            return new Program().RunAsync(args);
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
        }
    }

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

    [Command("test")]
    class TestCommand : Command
    {
        private readonly IConsole _console;
        private readonly TokenCredentialProvider _tokenProvider;

        public TestCommand(IConsole console, TokenCredentialProvider tokenProvider)
        {
            _console = console;
            _tokenProvider = tokenProvider;
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            var creds = await _tokenProvider.GetCredentialAsync();
            var token = await creds.GetTokenAsync(new TokenRequestContext(new []
            {
                "https://servicebus.azure.net/.default",
            }), cancellationToken);
            Debug.WriteLine(token.ExpiresOn);
            _console.WriteImportant("Successfully authenticated");
        }
    }
}
