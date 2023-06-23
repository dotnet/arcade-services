using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DncEng.CommandLineLib;
using Microsoft.DncEng.Configuration.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.DncEng.SecretManager;

public class SettingsFileValidator
{
    private readonly IConsole _console;
    private readonly SecretTypeRegistry _secretTypeRegistry;

    public SettingsFileValidator(IConsole console, SecretTypeRegistry secretTypeRegistry)
    {
        _console = console;
        _secretTypeRegistry = secretTypeRegistry;
    }

    public async Task<bool> ValidateFileAsync(string envSettingsFile, string baseSettingsFile, string manifestFile, CancellationToken cancellationToken)
    {
        _console.WriteLine($"Verifying secret references in file pair ({envSettingsFile}, {baseSettingsFile}) with secret manifest {manifestFile}");
        SecretManifest manifest = SecretManifest.Read(manifestFile);
        var availableSecrets = new HashSet<string>();
        foreach (var (baseName, secret) in manifest.Secrets)
        {
            using SecretType.Bound secretType = _secretTypeRegistry.Get(secret.Type).BindParameters(secret.Parameters);
            List<string> names = secretType.GetCompositeSecretSuffixes().Select(suffix => baseName + suffix).ToList();
            foreach (string name in names)
            {
                availableSecrets.Add(name);
            }
        }

        var settings = await ReadSettingsPair(envSettingsFile, baseSettingsFile);


        bool foundError = false;
        foreach (var value in settings.Values)
        {
            MatchCollection matches = KeyVaultConfigMapper.VaultReferenceRegex.Matches(value.ToString());
            foreach (Match match in matches)
            {
                string key = match.Groups["key"].Value;
                if (!availableSecrets.Contains(key))
                {
                    var sourceFile = SourceFile.GetOrCreateValue(value);
                    var line = ((IJsonLineInfo) value).LineNumber;
                    // LinePosition is the "end" of the JToken, so we have to find the start
                    var column = ((IJsonLineInfo) value).LinePosition - value.ToString().Length;
                    _console.LogError($"Secret '{key}' does not exist in manifest file.\n", sourceFile, line, column + match.Groups["key"].Index);
                    foundError = true;
                }
            }
        }

        return !foundError;
    }

    private async Task<Dictionary<string, JToken>> ReadSettingsPair(string envSettingsFile, string baseSettingsFile)
    {
        Dictionary<string, JToken> envSettings = await ReadSettingsFile(envSettingsFile);
        Dictionary<string, JToken> baseSettings = await ReadSettingsFile(baseSettingsFile);
        CoalesceInto(envSettings, baseSettings);
        return envSettings;
    }

    private void CoalesceInto(Dictionary<string, JToken> left, Dictionary<string, JToken> right)
    {
        foreach (var key in right.Keys)
        {
            if (!left.ContainsKey(key))
            {
                left[key] = right[key];
            }
        }
    }

    private static readonly ConditionalWeakTable<JToken, string> SourceFile = new ConditionalWeakTable<JToken, string>();

    private async Task<Dictionary<string, JToken>> ReadSettingsFile(string settingsFile)
    {
        var result = new Dictionary<string, JToken>();
        using var r = new StreamReader(settingsFile);
        using var jr = new JsonTextReader(r);

        var obj = await JObject.LoadAsync(jr);

        void VisitToken(string prefix, JToken value)
        {
            if (value is JObject subObj)
            {
                VisitObj($"{prefix}|", subObj);
            }
            else if (value is JArray arr)
            {
                VisitArray($"{prefix}|", arr);
            }
            else
            {
                SourceFile.Add(value, settingsFile);
                result.Add(prefix, value);
            }
        }

        void VisitObj(string prefix, JObject obj)
        {
            foreach (var (key, value) in obj)
            {
                VisitToken($"{prefix}{key}", value);
            }
        }

        void VisitArray(string prefix, JArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var value = arr[i];
                VisitToken($"{prefix}{i}", value);
            }
        }

        VisitObj("", obj);
        return result;
    }
}
