using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.SecretManager.StorageTypes;
using Mono.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Command = Microsoft.DncEng.CommandLineLib.Command;

namespace Microsoft.DncEng.SecretManager.Commands;

[Command("validate-all")]
public class ValidateAllCommand : Command
{
    private readonly IConsole _console;
    private readonly SettingsFileValidator _settingsFileValidator;
    private readonly StorageLocationTypeRegistry _storageLocationTypeRegistry;
    private readonly List<string> _manifestFiles = new List<string>();
    private string _basePath;

    public ValidateAllCommand(IConsole console, SettingsFileValidator settingsFileValidator, StorageLocationTypeRegistry storageLocationTypeRegistry)
    {
        _console = console;
        _settingsFileValidator = settingsFileValidator;
        _storageLocationTypeRegistry = storageLocationTypeRegistry;
    }

    public override OptionSet GetOptions()
    {
        return new OptionSet
        {
            {"m|manifest-file=", "A secret manifest file. Can be specified more than once.", m => _manifestFiles.Add(m)},
            {"b|base-path=", "The base path to search for settings files.", b => _basePath = b},
        };
    }

    public override bool AreRequiredOptionsSet()
    {
        return !string.IsNullOrEmpty(_basePath) && _manifestFiles.Any();
    }

    public override async Task RunAsync(CancellationToken cancellationToken)
    {
        bool haveErrors = false;
        var manifestFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string manifestFile in _manifestFiles)
        {
            SecretManifest manifest = SecretManifest.Read(manifestFile);
            StorageLocationType storageType = _storageLocationTypeRegistry.Get(manifest.StorageLocation.Type);
            if (!(storageType is AzureKeyVault azureKeyVaultStorageType))
            {
                _console.WriteImportant($"Skipping non-azure-key-vault manifest {manifestFile}", ConsoleColor.Yellow);
                continue;
            }

            string vaultUri = azureKeyVaultStorageType.GetAzureKeyVaultUri(ParameterConverter.ConvertParameters<AzureKeyVaultParameters>(manifest.StorageLocation.Parameters));

            manifestFiles.Add(vaultUri, manifestFile);
        }


        var settingsFiles = new List<(FileInfo environmentFile, FileInfo baseFile)>();
        foreach (string jsonFile in Directory.EnumerateFiles(_basePath, "settings.json", SearchOption.AllDirectories))
        {
            var baseFile = new FileInfo(jsonFile);
            foreach (string envFile in Directory.EnumerateFiles(baseFile.DirectoryName, "settings.*.json"))
            {
                settingsFiles.Add((new FileInfo(envFile), baseFile));
            }
        }

        foreach (var (envFile, baseFile) in settingsFiles)
        {
            string specifiedVaultUri =
                await ReadVaultUriFromSettingsFile(envFile.FullName) ??
                await ReadVaultUriFromSettingsFile(baseFile.FullName);

            if (string.IsNullOrEmpty(specifiedVaultUri))
            {
                _console.LogError($"Settings file pair ({envFile}, {baseFile}) has no vault uri.", envFile.FullName, 0, 0);
                haveErrors = true;
                continue;
            }

            _console.WriteLine($"Settings file pair ({envFile}, {baseFile}) has vault uri {specifiedVaultUri}");

            if (!manifestFiles.TryGetValue(specifiedVaultUri, out string manifestFile))
            {
                _console.LogError($"Vault Uri {specifiedVaultUri} does not have a matching manifest.", envFile.FullName, 0, 0);
                haveErrors = true;
                continue;
            }

            haveErrors |= !await _settingsFileValidator.ValidateFileAsync(envFile.FullName, baseFile.FullName, manifestFile, cancellationToken);
        }

        if (haveErrors)
        {
            throw new FailWithExitCodeException(77);
        }
    }

    private async Task<string> ReadVaultUriFromSettingsFile(string settingsFile)
    {
        using var r = new StreamReader(settingsFile);
        using var jr = new JsonTextReader(r);
        JObject obj = await JObject.LoadAsync(jr);
        return obj.Value<string>("KeyVaultUri");
    }
}
