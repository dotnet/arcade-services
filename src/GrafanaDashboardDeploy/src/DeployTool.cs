// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DotNet.Grafana;
using Microsoft.Azure.KeyVault.Models;

namespace Microsoft.DotNet.Grafana
{
    public sealed class DeployTool : IDisposable
    {
        private const string DashboardExtension = ".dashboard.json";
        private const string DatasourceExtension = ".datasource.json";

        private readonly string _dashboardDirectory;
        private readonly string _datasourceDirectory;
        private readonly Lazy<KeyVaultClient> _keyVault;

        private KeyVaultClient KeyVault => _keyVault.Value;

        public DeployTool(string dashboardDirectory, string datasourceDirectory)
        {
            _dashboardDirectory = dashboardDirectory;
            _keyVault = new Lazy<KeyVaultClient>(GetKeyVaultClient);
            _datasourceDirectory = datasourceDirectory;
        }

        public async Task PostToGrafana(GrafanaClient grafanaClient, string inputFilePath)
        {
            JArray folderArray = await grafanaClient.ListFoldersAsync().ConfigureAwait(false);
            List<FolderData> folders = folderArray.Select(f => new FolderData(f.Value<string>("uid"), f.Value<string>("title")))
                .ToList();

            foreach (string datasourcePath in Directory.GetFiles(_datasourceDirectory,
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

                await grafanaClient.CreateDatasourceAsync(data).ConfigureAwait(false);
            }

            foreach (string dashboardPath in Directory.GetFiles(_dashboardDirectory,
                "*" + DashboardExtension,
                SearchOption.AllDirectories))
            {

                string folderName = Path.GetDirectoryName(dashboardPath);

                FolderData folder = folders.FirstOrDefault(f => f.Title == folderName);
                
                JObject result = await grafanaClient.CreateFolderAsync(folderName, folderName).ConfigureAwait(false);
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

                JObject tagObject = null;
                if (data.TryGetValue("tags", out JToken tagToken))
                {
                    tagObject = tagToken as JObject;
                }

                if (tagObject == null)
                {
                    data["tags"] = tagObject = new JObject();
                }

                tagObject["uid"] = data.Value<string>("uid");
                await grafanaClient.CreateDashboardAsync(data, folderId);
            }
        }

        public async Task ImportFromGrafana(GrafanaClient grafanaClient, IEnumerable<string> dashboardUids)
        {
            foreach (string dashboardUid in dashboardUids)
            {
                // Get a dashboard
                JObject dashboard = await grafanaClient.GetDashboardAsync(dashboardUid).ConfigureAwait(false);

                // Cache dashboard data needed for post
                JObject slimmedDashboard = GrafanaSerialization.SanitizeDashboard(dashboard);

                // Get the dashboard folder data and cache if not already present
                int folderId = GrafanaSerialization.ExtractFolderId(dashboard);
                JObject folder = await grafanaClient.GetFolderAsync(folderId).ConfigureAwait(false);
                FolderData folderData = GrafanaSerialization.SanitizeFolder(folder);

                // Folder uid is needed for the dashboard export object                    
                JObject dashboardObject = GrafanaSerialization.ConstructDashboardExportObject(slimmedDashboard, folderData.Uid);

                // Get datasources used in the dashboard
                IEnumerable<string> dataSourceNames = GrafanaSerialization.ExtractDataSourceNames(dashboard);

                foreach (string datasourceName in dataSourceNames)
                {
                    string datasourcePath = GetDatasourceFilePath(datasourceName);
                    if (File.Exists(datasourcePath))
                    {
                        // If we already have that datasource, don't overwrite it's useful values with empty ones
                        continue;
                    }

                    JObject datasource = await grafanaClient.GetDataSource(datasourceName).ConfigureAwait(false);
                    datasource = GrafanaSerialization.SanitizeDataSource(datasource);
                    using (var datasourceStreamWriter = new StreamWriter(datasourcePath))
                    using (var datasourceJsonWriter = new JsonTextWriter(datasourceStreamWriter))
                    {
                        await datasource.WriteToAsync(datasourceJsonWriter);
                    }
                }


                string dashboardPath = GetDashboardFilePath(folderData.Title, dashboard.Value<string>("uid"));
                using (var dashboardStreamWriter = new StreamWriter(dashboardPath))
                using (var dashboardJsonWriter = new JsonTextWriter(dashboardStreamWriter))
                {
                    await dashboardObject.WriteToAsync(dashboardJsonWriter);
                }
            }
        }

        private string GetDashboardFilePath(string folder, string uid)
        {
            return Path.Combine(_dashboardDirectory, folder, uid + DashboardExtension);
        }

        private string GetDatasourceFilePath(string datasourceName)
        {
            return Path.Combine(_datasourceDirectory, datasourceName + DatasourceExtension);
        }


        private static bool TryGetSecretName(string data, out string secret)
        {
            Regex r = new Regex(@"\[[vV]ault\((.*)\)\]");
            var match = r.Match(data);

            if (!match.Success)
            {
                secret = String.Empty;
                return false;
            }

            secret = match.Groups[1].Value;
            return true;
        }

        private async Task<string> GetSecretAsync(string name)
        {
            SecretBundle result = await KeyVault.GetSecretAsync("https://dotnet-grafana.vault.azure.net/", name).ConfigureAwait(false);
            return result.Value;
        }

        private KeyVaultClient GetKeyVaultClient()
        {
            var tokenProvider = new AzureServiceTokenProvider();
            return new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(tokenProvider.KeyVaultTokenCallback));
        }

        public void Dispose()
        {
            if (_keyVault.IsValueCreated)
            {
                _keyVault.Value.Dispose();
            }
        }
    }
}
