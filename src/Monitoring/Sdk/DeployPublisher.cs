// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.DotNet.Monitoring.Sdk;

public sealed class DeployPublisher : DeployToolBase, IDisposable
{
    private readonly string _tenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47"; // microsoft.onmicrosoft.com
    private readonly string _keyVaultName;
    private readonly string _servicePrincipalId;
    private readonly string _servicePrincipalSecret;
    private readonly Lazy<SecretClient> _keyVault;
    private readonly string _environment;
    private readonly string _parameterFile;

    private SecretClient KeyVault => _keyVault.Value;

    public DeployPublisher(
        GrafanaClient grafanaClient,
        string keyVaultName,
        string servicePrincipalId,
        string servicePrincipalSecret,
        string sourceTagValue,
        string dashboardDirectory,
        string datasourceDirectory,
        string notificationDirectory,
        string environment,
        string parametersFile,
        TaskLoggingHelper log) : base(
        grafanaClient, sourceTagValue, dashboardDirectory, datasourceDirectory, notificationDirectory, log)
    {
        _keyVaultName = keyVaultName;
        _servicePrincipalId = servicePrincipalId;
        _servicePrincipalSecret = servicePrincipalSecret;
        _environment = environment;
        _keyVault = new Lazy<SecretClient>(GetKeyVaultClient);
        _parameterFile = parametersFile;
    }
        
    private string EnvironmentDatasourceDirectory => Path.Combine(DatasourceDirectory, _environment);
    private string EnvironmentNotificationDirectory => Path.Combine(NotificationDirectory, _environment);

    public void Dispose()
    {
        // Nothing to dispose of
    }

    public async Task PostToGrafanaAsync()
    {
        await PostDatasourcesAsync().ConfigureAwait(false);

        await PostNotificationsAsync().ConfigureAwait(false);

        await PostDashboardsAsync().ConfigureAwait(false);
    }

    private async Task PostDatasourcesAsync()
    {
        foreach (string datasourcePath in Directory.GetFiles(EnvironmentDatasourceDirectory,
                     "*" + DatasourceExtension,
                     SearchOption.AllDirectories))
        {
            var name = GetNameFromDatasourceFile(Path.GetFileName(datasourcePath));
            JObject data;
            using (var sr = new StreamReader(datasourcePath))
            using (var jr = new JsonTextReader(sr))
            {
                data = await JObject.LoadAsync(jr).ConfigureAwait(false);
            }

            data["name"] = name;

            Log.LogMessage(MessageImportance.Normal, "Posting datasource {0}...", name);

            await ReplaceVaultAsync(data);

            await GrafanaClient.CreateDatasourceAsync(data).ConfigureAwait(false);
        }
    }

    private async Task PostNotificationsAsync()
    {
        foreach (string notificationPath in Directory.GetFiles(EnvironmentNotificationDirectory,
                     "*" + NotificationExtension,
                     SearchOption.AllDirectories))
        {
            string uid = GetUidFromNotificationFile(Path.GetFileName(notificationPath));

            JObject data;
            using (var sr = new StreamReader(notificationPath))
            using (var jr = new JsonTextReader(sr))
            {
                data = await JObject.LoadAsync(jr).ConfigureAwait(false);
            }

            data["uid"] = uid;
            Log.LogMessage(MessageImportance.Normal, "Posting notification {0}...", uid);

            await ReplaceVaultAsync(data);

            await GrafanaClient.CreateNotificationChannelAsync(data).ConfigureAwait(false);
        }
    }

    private async Task PostDashboardsAsync()
    {
        JArray folderArray = await GrafanaClient.ListFoldersAsync().ConfigureAwait(false);
        List<FolderData> folders = folderArray.Select(f => new FolderData(f.Value<string>("uid"), f.Value<string>("title")))
            .ToList();
        var knownUids = new HashSet<string>();

        List<Parameter> parameters;

        using (StreamReader sr = new StreamReader(_parameterFile))
        using (JsonReader jr = new JsonTextReader(sr))
        {
            JsonSerializer jsonSerializer = new JsonSerializer();
            parameters = jsonSerializer.Deserialize<List<Parameter>>(jr);
        }

        foreach (string dashboardPath in GetAllDashboardPaths())
        {
            string folderName = Path.GetFileName(Path.GetDirectoryName(dashboardPath));
            string dashboardFileName = Path.GetFileName(dashboardPath);
            string uid = GetUidFromDashboardFile(dashboardFileName);
            knownUids.Add(uid);

            FolderData folder = folders.FirstOrDefault(f => f.Title == folderName);

            JObject result = await GrafanaClient.CreateFolderAsync(folderName, folderName).ConfigureAwait(false);
            string folderUid = result["uid"].Value<string>();
            int folderId = result["id"].Value<int>();

            if (folder == null)
            {
                folder = new FolderData(folderUid, folderName);
            }

            folder.Id = folderId;

            JObject data;
            using (var sr = new StreamReader(dashboardPath))
            using (var jr = new JsonTextReader(sr))
            {
                data = await JObject.LoadAsync(jr).ConfigureAwait(false);
            }

            JArray tagArray = null;
            if (data.TryGetValue("tags", out JToken tagToken))
            {
                tagArray = tagToken as JArray;
            }

            if (tagArray == null)
            {
                tagArray = new JArray();
            }

            var newTags = new JArray();
            foreach (JToken tag in tagArray)
            {
                if (tag.Value<string>().StartsWith(BaseUidTagPrefix) ||
                    tag.Value<string>().StartsWith(SourceTagPrefix))
                {
                    continue;
                }

                newTags.Add(tag);
            }

            tagArray.Add(GetUidTag(uid));
            tagArray.Add(SourceTag);
            data["tags"] = newTags;
            data["uid"] = uid;

            data = GrafanaSerialization.DeparameterizeDashboard(data, parameters, _environment);

            Log.LogMessage(MessageImportance.Normal, "Posting dashboard {0}...", uid);

            await GrafanaClient.CreateDashboardAsync(data, folderId).ConfigureAwait(false);
        }

        await ClearExtraneousDashboardsAsync(knownUids);
    }

    private async Task ClearExtraneousDashboardsAsync(HashSet<string> knownUids)
    {
        JArray allTagged = await GrafanaClient.SearchDashboardsByTagAsync(SourceTag).ConfigureAwait(false);
        HashSet<string> toRemove =  new HashSet<string>(allTagged.Where(IsManagedDashboard).Select(d => d.Value<string>("uid")));

        // We shouldn't remove the ones we just deployed
        toRemove.ExceptWith(knownUids);

        foreach (string uid in toRemove)
        {
            Log.LogMessage(MessageImportance.Normal, "Deleting extra dashboard {0}...", uid);
            await GrafanaClient.DeleteDashboardAsync(uid).ConfigureAwait(false);
        }
    }

    private static bool IsManagedDashboard(JToken d)
    {
        string uid = d.Value<string>("uid");
        // If the uid tag (which we set whenever we publish) doesn't match, that means someone copied it
        // so it's not managed by us. If it does match, that means it is managed and we deployed it
        return uid == d.Value<JObject>()?.Value<string>(GetUidTag(uid));
    }

    public async Task<JToken> ReplaceVaultAsync(JToken data)
    {
        switch (data)
        {
            case JObject jObject:
                foreach (var (key, value) in jObject)
                {
                    jObject[key] = await ReplaceVaultAsync(value);
                }
                return jObject;

            case JArray jArray:
                for (int i = 0; i < jArray.Count; i++)
                {
                    jArray[i] = await ReplaceVaultAsync(jArray[i]);
                }
                return jArray;

            case JValue jValue:
            {
                if (jValue.Type != JTokenType.String ||
                    !TryGetSecretName((string)jValue.Value, out string secretName))
                {
                    return jValue;
                }

                return await GetSecretAsync(secretName).ConfigureAwait(false);
            }
            default:
                return data;
        }
    }

    private static bool TryGetSecretName(string data, out string secret)
    {
        var r = new Regex(@"\[[vV]ault\((.*)\)\]");
        Match match = r.Match(data);

        if (!match.Success)
        {
            secret = null;
            return false;
        }

        secret = match.Groups[1].Value;
        return true;
    }

    private async Task<string> GetSecretAsync(string name)
    {
        KeyVaultSecret result = await KeyVault.GetSecretAsync(name).ConfigureAwait(false);
        return result.Value;
    }


    private SecretClient GetKeyVaultClient()
    {
        Uri vaultUri = new($"https://{_keyVaultName}.vault.azure.net/");
        TokenCredential credential = new ClientSecretCredential(_tenantId, _servicePrincipalId, _servicePrincipalSecret);
        return new SecretClient(vaultUri, credential);
    }
}
