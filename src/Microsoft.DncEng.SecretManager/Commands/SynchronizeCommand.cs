using System;
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
                bool confirmed = await _console.ConfirmAsync(
                    "-force is set, this will rotate every secret that exists, possibly causing service disruption. Continue? ");
                if (!confirmed)
                {
                    return;
                }
            }

            DateTimeOffset now = _clock.UtcNow;
            SecretManifest manifest = SecretManifest.Read(_manifestFile);
            using StorageLocationType.Bound storage = _storageLocationTypeRegistry.Get(manifest.StorageLocation.Type).BindParameters(manifest.StorageLocation.Parameters);
            using var disposables = new DisposableList();
            var references = new Dictionary<string, StorageLocationType.Bound>();
            foreach (var (name, storageReference) in manifest.References)
            {
                var bound = _storageLocationTypeRegistry.Get(storageReference.Type).BindParameters(storageReference.Parameters);
                disposables.Add(bound);
                references.Add(name, bound);
            }
            Dictionary<string, SecretProperties> existingSecrets = (await storage.ListSecretsAsync()).ToDictionary(p => p.Name);
            foreach (var (name, secret) in manifest.Secrets)
            {
                _console.WriteLine($"Synchronizing secret {name}, type {secret.Type}");
                using SecretType.Bound secretType = _secretTypeRegistry.Get(secret.Type).BindParameters(secret.Parameters);
                List<string> names = secretType.GetCompositeSecretSuffixes().Select(suffix => name + suffix).ToList();
                var existing = new List<SecretProperties>();
                foreach (string n in names)
                {
                    existingSecrets.TryGetValue(n, out SecretProperties e);
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
                    DateTimeOffset nextRotation = existing.Select(e => e.NextRotationOn).Min();
                    DateTimeOffset expires = existing.Select(e => e.ExpiresOn).Min();
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
                    _console.WriteLine($"Generating new value(s) for secret {name}...");
                    SecretProperties primary = existing.FirstOrDefault(p => p != null);
                    IImmutableDictionary<string, string> currentTags = primary?.Tags ?? ImmutableDictionary.Create<string, string>();
                    var context = new RotationContext(name, currentTags, storage, references);
                    List<SecretData> newValues = await secretType.RotateValues(context, cancellationToken);
                    IImmutableDictionary<string, string> newTags = context.GetValues();
                    _console.WriteLine("Done.");
                    _console.WriteLine($"Storing new value(s) in storage for secret {name}...");
                    foreach (var (n, value) in names.Zip(newValues))
                    {
                        await storage.SetSecretValueAsync(n, new SecretValue(value.Value, newTags, value.NextRotationOn, value.ExpiresOn));
                    }
                    _console.WriteLine("Done.");
                }
            }

            foreach (var (name, key) in manifest.Keys)
            {
                await storage.EnsureKeyAsync(name, key);
            }
        }
    }
}
