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

namespace DotNet.Grafana
{
    public static class DeployTool
    {
        public static async Task PostExportPack(GrafanaClient grafanaClient, string inputFilePath)
        {
            var folderIdMap = new Dictionary<string, int>();

            using var sr = new StreamReader(inputFilePath);
            using var jr = new JsonTextReader(sr);
            var data = await JObject.LoadAsync(jr).ConfigureAwait(false);

            foreach (var folder in data["folders"])
            {
                string folderUid = folder["uid"].Value<string>();
                string folderTitle = folder["title"].Value<string>();

                var result = await grafanaClient.CreateFolderAsync(folderUid, folderTitle).ConfigureAwait(false);
                int folderId = result["id"].Value<int>();

                folderIdMap.Add(folderUid, folderId);
            }

            foreach (var datasource in data["datasources"])
            {
                foreach (var secretData in datasource["secureJsonData"].Children<JProperty>())
                {
                    string secretExpression = secretData.Value.ToString();
                    string secretName;
                    string secretValue;

                    if (!TryGetSecretName(secretExpression, out secretName))
                    {
                        continue;
                    }

                    secretValue = await GetSecretAsync(secretName).ConfigureAwait(false);
                    secretData.Value = secretValue;
                }

                await grafanaClient.CreateDatasourceAsync(datasource.ToObject<JObject>()).ConfigureAwait(false);
            }

            foreach (var dashboard in data["dashboards"])
            {
                string dashboardFolderUid = dashboard["meta"]["folderUid"].Value<string>();

                int folderId = folderIdMap[dashboardFolderUid];

                await grafanaClient.CreateDashboardAsync(dashboard["dashboard"].ToObject<JObject>(), folderId).ConfigureAwait(false);
            }
        }

        public static async Task MakeExportPack(GrafanaClient grafanaClient, IEnumerable<string> dashboardUids, string outputFilePath)
        {
            var dashboards = new List<JObject>();
            var folders = new List<JObject>();
            var dataSources = new List<JObject>();

            foreach (var dashboardUid in dashboardUids)
            {
                // Get a dashboard
                JObject dashboard = await grafanaClient.GetDashboardAsync(dashboardUid).ConfigureAwait(false);

                // Cache dashboard data needed for post
                JObject slimmedDashboard = Util.SanitizeDashboard(dashboard);

                // Get the dashboard folder data and cache if not already present
                int folderId = Util.ExtractFolderId(dashboard);
                var folder = await grafanaClient.GetFolder(folderId).ConfigureAwait(false);                

                // Cache if not already present
                if (!folders.Any(v => v.SelectToken("$.uid").Value<string>() == folder["uid"].Value<string>()))
                {
                    var slimmedFolder = Util.SanitizeFolder(folder);
                    folders.Add(slimmedFolder);
                }

                // Folder uid is needed for the dashboard export object                    

                var dashboardObject = Util.ConstructDashboardExportObject(slimmedDashboard, folder["uid"].Value<string>());
                dashboards.Add(dashboardObject);

                // Get datasources used in the dashboard
                var dataSourceNames = Util.ExtractDataSourceNames(dashboard);

                foreach (var datasourceName in dataSourceNames)
                {
                    // Cache if not already present
                    if (!dataSources.Any(v => v.SelectToken("$.name").Value<string>() == datasourceName))
                    {
                        var dataSource = await grafanaClient.GetDataSource(datasourceName).ConfigureAwait(false);
                        dataSource = Util.SanitizeDataSource(dataSource);
                        dataSources.Add(dataSource);
                    }
                }
            }

            JObject output = new JObject();
            output.Add("folders", new JArray(folders));
            output.Add("dashboards", new JArray(dashboards));
            output.Add("datasources", new JArray(dataSources));

            using var outFile = new StreamWriter(outputFilePath);
            using var writer = new JsonTextWriter(outFile);
            await output.WriteToAsync(writer);
        }

        

        /// <summary>
        /// Extract the name of a secret from a marker in the form of [vault(secretName)]
        /// </summary>
        public static bool TryGetSecretName(string data, out string secret)
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

        private static async Task<string> GetSecretAsync(string name)
        {
            // TODO: This whole thing should be moved to a seperate secrets manager

            // Instantiate a new KeyVaultClient object, with an access token to Key Vault
            var azureServiceTokenProvider1 = new AzureServiceTokenProvider();
            using var kv = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider1.KeyVaultTokenCallback));

            var result = await kv.GetSecretAsync("https://dotnet-grafana.vault.azure.net/", name).ConfigureAwait(false);
            return result.Value;
        }
    }
}
