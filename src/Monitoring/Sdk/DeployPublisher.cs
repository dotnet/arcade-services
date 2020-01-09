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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public sealed class DeployPublisher : DeployToolBase, IDisposable
    {
        private readonly string _keyVaultName;
        private readonly string _keyVaultConnectionString;
        private readonly Lazy<KeyVaultClient> _keyVault;

        private KeyVaultClient KeyVault => _keyVault.Value;

        public DeployPublisher(
            GrafanaClient grafanaClient,
            string keyVaultName,
            string keyVaultConnectionString,
            string sourceTagValue,
            string dashboardDirectory,
            string datasourceDirectory) : base(
            grafanaClient, sourceTagValue, dashboardDirectory, datasourceDirectory)
        {
            _keyVaultName = keyVaultName;
            _keyVaultConnectionString = keyVaultConnectionString;
            _keyVault = new Lazy<KeyVaultClient>(GetKeyVaultClient);
        }

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
            await PostDatasourcesAsync();

            await PostDashboardsAsync();
        }

        private async Task PostDashboardsAsync()
        {
            JArray folderArray = await GrafanaClient.ListFoldersAsync().ConfigureAwait(false);
            List<FolderData> folders = folderArray.Select(f => new FolderData(f.Value<string>("uid"), f.Value<string>("title")))
                .ToList();
            List<string> knownUids = new List<string>();
            foreach (string dashboardPath in Directory.GetFiles(DashboardDirectory,
                "*" + DashboardExtension,
                SearchOption.AllDirectories))
            {
                string folderName = Path.GetDirectoryName(dashboardPath);
                string dashboardFileName = Path.GetFileName(dashboardPath);
                string uid = dashboardFileName.Substring(0, dashboardFileName.Length - DashboardExtension.Length);
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

                tagArray.Add(BaseUidTagPrefix + uid);
                tagArray.Add(SourceTagPrefix + SourceTagValue);
                data["tags"] = newTags;
                data["uid"] = uid;
                await GrafanaClient.CreateDashboardAsync(data, folderId);
            }
        }

        private async Task PostDatasourcesAsync()
        {
            foreach (string datasourcePath in Directory.GetFiles(DatasourceDirectory,
                "*" + DatasourceExtension,
                SearchOption.AllDirectories))
            {
                JObject data;
                using (var sr = new StreamReader(datasourcePath))
                using (var jr = new JsonTextReader(sr))
                {
                    data = await JObject.LoadAsync(jr).ConfigureAwait(false);
                }

                var secureJsonData = data.Value<JObject>("secureJsonData");
                foreach (var (key, value) in secureJsonData)
                {
                    if (!TryGetSecretName(value.Value<string>(), out string secretName))
                    {
                        continue;
                    }

                    secureJsonData[key] = await GetSecretAsync(secretName).ConfigureAwait(false);
                }

                await GrafanaClient.CreateDatasourceAsync(data).ConfigureAwait(false);
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
