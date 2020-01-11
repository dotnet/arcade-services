// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNet.Grafana;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public sealed class DeployPublisher : DeployToolBase, IDisposable
    {
        private readonly string _keyVaultName;
        private readonly string _keyVaultConnectionString;
        private readonly Lazy<KeyVaultClient> _keyVault;
        private readonly string _environment;

        private KeyVaultClient KeyVault => _keyVault.Value;

        public DeployPublisher(
            GrafanaClient grafanaClient,
            string keyVaultName,
            string keyVaultConnectionString,
            string sourceTagValue,
            string dashboardDirectory,
            string datasourceDirectory,
            string notificationDirectory,
            string environment,
            TaskLoggingHelper log) : base(
            grafanaClient, sourceTagValue, dashboardDirectory, datasourceDirectory, notificationDirectory, log)
        {
            _keyVaultName = keyVaultName;
            _keyVaultConnectionString = keyVaultConnectionString;
            _environment = environment;
            _keyVault = new Lazy<KeyVaultClient>(GetKeyVaultClient);
        }
        
        private string EnvironmentDatasourceDirectory => Path.Combine(DatasourceDirectory, _environment);
        private string EnvironmentNotificationDirectory => Path.Combine(NotificationDirectory, _environment);

        public void Dispose()
        {
            // If it's not already created, don't create it just to dispose it
            if (_keyVault.IsValueCreated)
            {
                _keyVault.Value.Dispose();
            }
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

                JObject existing = await GrafanaClient.GetDataSourceAsync(name);
                await GrafanaClient.CreateDatasourceAsync(data).ConfigureAwait(false);
            }
        }

        private async Task PostNotificationsAsync()
        {
            foreach (string notificationPath in Directory.GetFiles(EnvironmentNotificationDirectory,
                "*" + DatasourceExtension,
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

                Log.LogMessage(MessageImportance.Normal, "Posting dashboard {0}...", uid);

                await GrafanaClient.CreateDashboardAsync(data, folderId).ConfigureAwait(false);
            }

            await ClearExtraneousDashboardsAsync(knownUids);
        }

        private async Task ClearExtraneousDashboardsAsync(HashSet<string> knownUids)
        {
            JArray allTagged = await GrafanaClient.SearchDashboardsByTagAsync(SourceTag).ConfigureAwait(false);
            HashSet<string> toRemove =  allTagged.Where(IsManagedDashboard).Select(d => d.Value<string>("uid")).ToHashSet();

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
            SecretBundle result = await KeyVault.GetSecretAsync($"https://{_keyVaultName}.vault.azure.net/", name).ConfigureAwait(false);
            return result.Value;
        }

        private KeyVaultClient GetKeyVaultClient()
        {
            var tokenProvider = new AzureServiceTokenProvider(_keyVaultConnectionString);
            return new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback));
        }
    }
}
