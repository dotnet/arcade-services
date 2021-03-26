using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;

namespace Microsoft.DncEng.SecretManager.Commands
{
    [Command("synchronize")]
    public class SynchronizeCommand : Command
    {
        private readonly StorageLocationTypeRegistry _storageLocationTypeRegistry;
        private readonly SecretTypeRegistry _secretTypeRegistry;
        private readonly ISystemClock _clock;

        public SynchronizeCommand(StorageLocationTypeRegistry storageLocationTypeRegistry, SecretTypeRegistry secretTypeRegistry, ISystemClock clock)
        {
            _storageLocationTypeRegistry = storageLocationTypeRegistry;
            _secretTypeRegistry = secretTypeRegistry;
            _clock = clock;
        }

        private string _manifestFile;

        public override List<string> HandlePositionalArguments(List<string> args)
        {
            _manifestFile = ConsumeIfNull(_manifestFile, args);
            return base.HandlePositionalArguments(args);
        }

        public override async Task RunAsync(CancellationToken cancellationToken)
        {
            var now = _clock.UtcNow;
            var manifest = SecretManifest.Read(_manifestFile);
            using var storage = _storageLocationTypeRegistry.Create(manifest.StorageLocation.Type, manifest.StorageLocation.Parameters);
            var existingSecrets = (await storage.ListSecretsAsync()).ToDictionary(p => p.Name);
            foreach (var (name, secret) in manifest.Secrets)
            {
                var secretType = _secretTypeRegistry.Create(secret.Type, secret.Parameters);
                var names = secretType.GetCompositeSecretSuffixes().Select(suffix => name + suffix).ToList();
                var existing = new List<SecretProperties>();
                foreach (var n in names)
                {
                    existingSecrets.TryGetValue(n, out var e);
                    existing.Add(e); // here we intentionally ignore the result of TryGetValue because we want to add null to the list to represent "this isn't in the store"
                }

                bool regenerate = false;

                if (existing.Any(e => e == null))
                {
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
                        // we have hit the rotation time, rotate
                        regenerate = true;
                    }
                    else if (expires <= now)
                    {
                        // the secret has expired, this shouldn't happen in normal operation but we should rotate
                        regenerate = true;
                    }
                }


                if (regenerate)
                {
                    var primary = existing.FirstOrDefault(p => p != null);
                    var currentTags = primary?.Tags ?? ImmutableDictionary.Create<string, string>();
                    var context = new RotationContext(currentTags);
                    var newValues = await secretType.RotateValues(context, cancellationToken);
                    var newTags = context.GetValues();
                    foreach (var (n, value) in names.Zip(newValues))
                    {
                        await storage.SetSecretValueAsync(n, new SecretValue(value.Value, newTags, value.NextRotationOn, value.ExpiresOn));
                    }
                }

            }

        }
    }
}
