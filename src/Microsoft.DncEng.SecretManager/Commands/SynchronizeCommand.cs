using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Mono.Options;
using Command = Microsoft.DncEng.CommandLineLib.Command;

namespace Microsoft.DncEng.SecretManager.Commands
{
    [Command("synchronize")]
    public class SynchronizeCommand : Command
    {
        private readonly StorageLocationTypeRegistry _storageLocationTypeRegistry;
        private readonly SecretTypeRegistry _secretTypeRegistry;
        private readonly ISystemClock _clock;
        private readonly IConsole _console;
        private bool _force = false;

        public SynchronizeCommand(StorageLocationTypeRegistry storageLocationTypeRegistry, SecretTypeRegistry secretTypeRegistry, ISystemClock clock, IConsole console)
        {
            _storageLocationTypeRegistry = storageLocationTypeRegistry;
            _secretTypeRegistry = secretTypeRegistry;
            _clock = clock;
            _console = console;
        }

        private string _manifestFile;

        public override List<string> HandlePositionalArguments(List<string> args)
        {
            _manifestFile = ConsumeIfNull(_manifestFile, args);
            return base.HandlePositionalArguments(args);
        }

        public override OptionSet GetOptions()
        {
            return new OptionSet
            {
                {"f|force", "Force rotate all secrets", f => _force = !string.IsNullOrEmpty(f)},
            };
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            _console.WriteLine($"Synchronizing secrets contained in {_manifestFile}");
            if (_force)
            {
                var confirmed = await _console.ConfirmAsync(
                    "-force is set, this will rotate every secret that exists, possibly causing service disruption. Continue?");
                if (!confirmed)
                {
                    return;
                }
            }

            var now = _clock.UtcNow;
            var manifest = SecretManifest.Read(_manifestFile);
            using var storage = _storageLocationTypeRegistry.Create(manifest.StorageLocation.Type, manifest.StorageLocation.Parameters);
            var existingSecrets = (await storage.ListSecretsAsync()).ToDictionary(p => p.Name);
            foreach (var (name, secret) in manifest.Secrets)
            {
                _console.WriteLine($"Synchronizing secret {name}, type {secret.Type}");
                var secretType = _secretTypeRegistry.Create(secret.Type, secret.Parameters);
                var names = secretType.GetCompositeSecretSuffixes().Select(suffix => name + suffix).ToList();
                var existing = new List<SecretProperties>();
                foreach (var n in names)
                {
                    existingSecrets.TryGetValue(n, out var e);
                    existing.Add(e); // here we intentionally ignore the result of TryGetValue because we want to add null to the list to represent "this isn't in the store"
                }

                bool regenerate = false;

                if (_force)
                {
                    _console.WriteLine("-force is set, will rotate.");
                    regenerate = true;
                }
                else if (existing.Any(e => e == null))
                {
                    _console.WriteLine("Secret not found in storage, will create.");
                    // secret is missing from storage (either completely missing or partially missing)
                    regenerate = true;
                }
                else
                {
                    // If these fields aren't the same for every part of a composite secrets, assume the soonest value is right
                    var nextRotation = existing.Select(e => e.NextRotationOn).Min();
                    var expires = existing.Select(e => e.ExpiresOn).Min();
                    if (nextRotation <= now)
                    {
                        _console.WriteLine($"Secret scheduled for rotation on {nextRotation}, will rotate.");
                        // we have hit the rotation time, rotate
                        regenerate = true;
                    }
                    else if (expires <= now)
                    {
                        _console.WriteLine($"Secret expired on {expires}, will rotate.");
                        // the secret has expired, this shouldn't happen in normal operation but we should rotate
                        regenerate = true;
                    }
                }

                if (!regenerate)
                {
                    _console.WriteLine("Secret is fine.");
                }


                if (regenerate)
                {
                    _console.Write($"Generating new value(s) for secret {name}...");
                    var primary = existing.FirstOrDefault(p => p != null);
                    var currentTags = primary?.Tags ?? ImmutableDictionary.Create<string, string>();
                    var context = new RotationContext(name, currentTags, storage);
                    var newValues = await secretType.RotateValues(context, cancellationToken);
                    var newTags = context.GetValues();
                    _console.WriteLine(" Done.");
                    _console.Write($"Storing new value(s) in storage for secret {name}...");
                    foreach (var (n, value) in names.Zip(newValues))
                    {
                        await storage.SetSecretValueAsync(n, new SecretValue(value.Value, newTags, value.NextRotationOn, value.ExpiresOn));
                    }
                    _console.WriteLine(" Done.");
                }
            }
        }
    }
}
