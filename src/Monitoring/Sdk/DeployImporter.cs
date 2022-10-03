// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.DotNet.Monitoring.Sdk;

public sealed class DeployImporter : DeployToolBase
{
    private readonly string[] _environments;
    private readonly JsonSerializer _serializer = new JsonSerializer {Formatting = Formatting.Indented};
    private readonly string _parametersFilePath;
    private readonly string _environment;

    public DeployImporter(
        GrafanaClient grafanaClient,
        string sourceTagValue,
        string dashboardDirectory,
        string datasourceDirectory,
        string notificationDirectory,
        string[] environments,
        string parametersFilePath,
        string environment,
        TaskLoggingHelper log) : base(
        grafanaClient, sourceTagValue, dashboardDirectory, datasourceDirectory, notificationDirectory, log)
    {
        _environments = environments;
        _parametersFilePath = parametersFilePath;
        _environment = environment;
    }

    public Task ImportFromGrafana(params string[] dashboardUids)
    {
        return ImportFromGrafana((IEnumerable<string>) dashboardUids);
    }

    public async Task ImportFromGrafana(IEnumerable<string> dashboardUids)
    {
        HashSet<string> usedNotificationIds = new HashSet<string>();

        foreach (var dashboardPath in GetAllDashboardPaths())
        {
            if (dashboardUids.Any(d => d.Contains(GetUidFromDashboardFile(dashboardPath))))
            {
                // This is a new dashboard, don't assume it alert tags
                JObject data;
                using (var sr = new StreamReader(dashboardPath))
                using (var jr = new JsonTextReader(sr))
                {
                    data = await JObject.LoadAsync(jr).ConfigureAwait(false);
                }

                IEnumerable<string> notificationIds = data.SelectTokens("..alertRuleTags.NotificationId")
                    .Select(d => d.Value<string>());
                foreach (var alertId in notificationIds)
                {
                    usedNotificationIds.Add(alertId);
                }
            }
        }

        List<Parameter> parameters;

        if (File.Exists(_parametersFilePath))
        {
            using (var sr = new StreamReader(_parametersFilePath))
            using (var jr = new JsonTextReader(sr))
            {
                parameters = _serializer.Deserialize<List<Parameter>>(jr);
            }
        }
        else
        {
            parameters = new List<Parameter>();
        }

        foreach (string dashboardUid in dashboardUids)
        {
            // Get a dashboard
            JObject dashboard = await GrafanaClient.GetDashboardAsync(dashboardUid).ConfigureAwait(false);

            // Cache dashboard data needed for post

            string targetUid = dashboard.Value<JObject>("dashboard").Value<string>("uid");

            // SanitizeDashboard makes structural changes to the JSON and ParameterizeDashboard
            // expects a certain structure, so order matters when calling these methods.
            JObject parameterizedDashboard = GrafanaSerialization.ParameterizeDashboard(dashboard, parameters, _environments, _environment);
            JObject slimmedDashboard = GrafanaSerialization.SanitizeDashboard(parameterizedDashboard);

            UpdateNotificationIds(slimmedDashboard, usedNotificationIds);

            JArray tags = slimmedDashboard.Value<JArray>("tags");
            JToken uidTag = tags.FirstOrDefault(t => t.Value<string>().StartsWith(BaseUidTagPrefix));
            if (uidTag != null)
            {
                targetUid = uidTag.Value<string>().Substring(BaseUidTagPrefix.Length);
            }

            // Get the dashboard folder data and cache if not already present
            int folderId = GrafanaSerialization.ExtractFolderId(dashboard);
            JObject folder = await GrafanaClient.GetFolderAsync(folderId).ConfigureAwait(false);
            FolderData folderData = GrafanaSerialization.SanitizeFolder(folder);
                
            // Get datasources used in the dashboard
            IEnumerable<string> dataSourceIdentifiers = GrafanaSerialization.ExtractDataSourceIdentifiers(dashboard);

            foreach (string dataSourceIdentifier in dataSourceIdentifiers)
            {
                JObject datasource = await GrafanaClient.GetDataSourceByUidAsync(dataSourceIdentifier).ConfigureAwait(false) ??
                                     await GrafanaClient.GetDataSourceByNameAsync(dataSourceIdentifier).ConfigureAwait(false);
                string datasourceName = datasource.Value<string>("name");

                datasource = GrafanaSerialization.SanitizeDataSource(datasource);
                // Create the data source for each environment
                foreach (string env in _environments)
                {
                    string datasourcePath = GetDatasourcePath(env, datasourceName);
                    if (File.Exists(datasourcePath))
                    {
                        // If we already have that datasource, don't overwrite it's useful values with empty ones
                        continue;
                    }

                    Log.LogMessage(MessageImportance.Normal, "Importing datasource {0}...", datasourceName);

                    Directory.CreateDirectory(Path.GetDirectoryName(datasourcePath));
                    using (var datasourceStreamWriter = new StreamWriter(datasourcePath))
                    using (var datasourceJsonWriter = new JsonTextWriter(datasourceStreamWriter))
                    {
                        _serializer.Serialize(datasourceJsonWriter, datasource);
                    }
                }
            }

            HashSet<string> usedNotifications = new HashSet<string>(slimmedDashboard
                .SelectTokens("panels.[*].alert.notifications.[*].uid")
                .Select(t => t.Value<string>())
                .Where(uid => !string.IsNullOrEmpty(uid)));
            foreach (string notificationUid in usedNotifications)
            {
                JObject notificationChannel = await GrafanaClient.GetNotificationChannelAsync(notificationUid);
                notificationChannel = GrafanaSerialization.SanitizeNotificationChannel(notificationChannel);

                // Create the data source for each environment
                foreach (string env in _environments)
                {
                    string notificationPath = GetNotificationPath(env, notificationUid);
                    if (File.Exists(notificationPath))
                    {
                        // If we already have that notification, don't overwrite it's useful values with empty ones
                        continue;
                    }

                    Log.LogMessage(MessageImportance.Normal, "Importing notification channel {0}...", notificationUid);

                    if (notificationChannel.ContainsKey("password"))
                    {
                        var password = notificationChannel.GetValue("password").Value<string>();
                        if (!password.Contains("vault"))
                        {
                            Log.LogWarning($"Please replace the password token with a key vault reference inside {notificationPath}");
                        }
                    }
                        
                    Directory.CreateDirectory(Path.GetDirectoryName(notificationPath));
                    using (var datasourceStreamWriter = new StreamWriter(notificationPath))
                    using (var datasourceJsonWriter = new JsonTextWriter(datasourceStreamWriter))
                    {
                        _serializer.Serialize(datasourceJsonWriter, notificationChannel);
                    }
                }
            }

                
            Log.LogMessage(MessageImportance.Normal, "Importing dashboard {0}...", targetUid);
            string dashboardPath = GetDashboardFilePath(folderData.Title, targetUid);
            Directory.CreateDirectory(Path.GetDirectoryName(dashboardPath));

            using (var dashboardStreamWriter = new StreamWriter(dashboardPath))
            using (var dashboardJsonWriter = new JsonTextWriter(dashboardStreamWriter))
            {
                _serializer.Serialize(dashboardJsonWriter, slimmedDashboard);
            }
        }

        // Save parameters back to disk
        Log.LogMessage(MessageImportance.Normal, "Saving parameters {0}...", _parametersFilePath);
        using (var sr = new StreamWriter(_parametersFilePath))
        using (var jr = new JsonTextWriter(sr))
        {
            _serializer.Serialize(jr, parameters);
        }
    }

    private static void UpdateNotificationIds(JObject slimmedDashboard, HashSet<string> usedNotificationIds)
    {
        var alertTags = slimmedDashboard.SelectTokens("..alertRuleTags");
        foreach (var alertTag in alertTags)
        {
            string notificationId = alertTag.Value<string>("NotificationId");
            if (notificationId == null || !usedNotificationIds.Add(notificationId))
            {
                // We don't have one, or we already used it
                notificationId = Guid.NewGuid().ToString("N");
                alertTag["NotificationId"] = notificationId;
                usedNotificationIds.Add(notificationId);
            }
        }
    }
}
