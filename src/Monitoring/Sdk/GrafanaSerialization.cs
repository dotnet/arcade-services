// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    /// <summary>
    /// Utility class to hold methods manipulating JSON in ways specific to the Grafana API
    /// </summary>
    public static class GrafanaSerialization
    {
        /// <summary>
        /// Extract the Folder ID of a Dashboard from a JSON object returned by the api/dashboards/uid endpoint
        /// </summary>
        /// <param name="dashboard"></param>
        /// <returns></returns>
        public static int ExtractFolderId(JObject dashboard)
        {
            return dashboard["meta"]["folderId"].Value<int>();
        }

        /// <summary>
        /// Modify a Dashboard JSON object as retrieved from the Grafana API into
        /// something suitable to post back to the API
        /// </summary>
        public static JObject SanitizeDashboard(JObject dashboard)
        {
            var slimmedDashboard = new JObject((JObject)dashboard["dashboard"]);
            slimmedDashboard.Remove("id");
            slimmedDashboard.Remove("uid");
            slimmedDashboard.Remove("version");
            var allTargets = slimmedDashboard.SelectTokens("panels.[*].targets.[*]");
            foreach (JToken jToken in allTargets)
            {
                var target = (JObject) jToken;
                target.Remove("subscription");
            }
            return slimmedDashboard;
        }

        /// <summary>
        /// Extract the names of data sources used by a given dashboard. 
        /// </summary>
        /// <param name="dashboard">A JSON definition of a dashboard as delivered by the Grafana API</param>
        /// <returns></returns>
        public static IEnumerable<string> ExtractDataSourceNames(JObject dashboard)
        {
            // Panel data sources live in panel[*].datasource, unless the "Mixed Data source" 
            // feature is used. Then, get names from panel[*].target.datasource. 
            // Annotation data sources live in annotation.list[*].datasource. The 
            // data source "-- Grafana --" is the build-in source and should be ignored.

            // A datasource field will be null if it is depending on the "default"
            // datasource. This is an unsupported configuration.

            return dashboard
                .SelectTokens("$..datasource")
                .Values<string>()
                .Where(x => !String.IsNullOrEmpty(x))
                .Where(x => x != "-- Mixed --" && x != "-- Grafana --")
                .Distinct();
        }
        
        /// <summary>
        /// Modify a Data Source JSON object as retrieved from the Grafana API into
        /// something suitable to post back to the API
        /// </summary>
        public static JObject SanitizeDataSource(JObject datasource)
        {
            string datasourceName = datasource.Value<string>("name");

            var slimmedDatasource = new JObject(datasource);
            slimmedDatasource.Remove("id");
            slimmedDatasource.Remove("orgId");
            slimmedDatasource.Remove("version");
            slimmedDatasource.Remove("name");

            if (string.IsNullOrEmpty(slimmedDatasource.Value<string>("url")))
            {
                slimmedDatasource.Remove("url");
            }

            // Add an entry in secureJsonData for each secureJsonField and decorate as a KeyVault insert.
            var secureFields = datasource.Value<JObject>("secureJsonFields");
            if (secureFields == null)
            {
                return slimmedDatasource;
            }

            var secureJsonData = new JObject();
            foreach (var (name, _) in secureFields)
            {
                secureJsonData[name] = $"[vault(PLACEHOLDER:{datasourceName}:{name})]";
            }

            slimmedDatasource["secureJsonData"] = secureJsonData;
            slimmedDatasource.Remove("secureJsonFields");

            return slimmedDatasource;
        }

        public static FolderData SanitizeFolder(JObject folder)
        {
            return new FolderData(folder.Value<string>("uid"), folder.Value<string>("title"));
        }

        public static JObject SanitizeNotificationChannel(JObject notificationChannel)
        {
            notificationChannel.Remove("id");
            notificationChannel.Remove("uid");
            notificationChannel.Remove("created");
            notificationChannel.Remove("updated");
            return notificationChannel;
        }

        public static JObject ParameterizeDashboard(JObject dashboard, ICollection<Parameter> parameters, IEnumerable<string> environments, string activeEnvironment)
        {
            JObject parameterizedDashboard = new JObject(dashboard);

            // These paths may be parameterized
            string[] paths = new[]
            {
                "$.dashboard.panels[*].targets[*].azureMonitor.resourceGroup",
                "$.dashboard.panels[*].targets[*].azureMonitor.resourceName",
                "$.dashboard.panels[*].targets[*].azureLogAnalytics.resource",
                "$.dashboard.panels[*].targets[*].azureLogAnalytics.workspace"
            };

            foreach (string path in paths)
            {
                IEnumerable<JToken> tokens = parameterizedDashboard.SelectTokens(path);

                foreach (JToken token in tokens)
                {
                    string value = token.Value<string>();

                    // Value Can technically be other JSON types (but the paths specified above
                    // will always be strings). Because the object must be a "string" to store the
                    // parameter info, supporting anything other than a string will require also
                    // keeping type info. So only support strings for now.

                    if (value == null)
                    {
                        throw new Exception("Unexpected token type. Value must be a string.");
                    }

                    Parameter p = parameters
                        .Where(p => p.Values.TryGetValue(activeEnvironment, out var v) && v == value)
                        .FirstOrDefault();

                    if (p == null)
                    {
                        string name = Guid.NewGuid().ToString();

                        p = new Parameter()
                        {
                            Name = name,
                            Values = environments.Select(env =>
                            {
                                if (env == activeEnvironment)
                                {
                                    return (env, value);
                                }
                                else
                                {
                                    return (env, "PLACEHOLDER");
                                }
                            }).ToDictionary(e => e.env, e => e.Item2)
                        };

                        parameters.Add(p);
                    }

                    token.Replace(new JValue($"[parameter({p.Name})]"));
                }
            }

            return parameterizedDashboard;
        }

        public static JObject DeparameterizeDashboard(JObject dashboard, IEnumerable<Parameter> parameters, string environment)
        {
            JObject deparameterizedDashboard = new JObject(dashboard);

            IEnumerable<JToken> tokens = deparameterizedDashboard.SelectTokens("$..*")
                .Where(token => token.Type == JTokenType.String)
                .Where(token => TryGetParameterName(token.Value<string>(), out string _))
                .ToList(); // For some reason .SelectTokens throws if enumeration happens in the foreach

            foreach (JToken token in tokens)
            {
                TryGetParameterName(token.Value<string>(), out string parameterName);

                Parameter parameter = parameters.FirstOrDefault(param => param.Name == parameterName);

                if (parameter == null)
                {
                    throw new ArgumentException($"Dashboard contains parameter \"{parameterName}\" but no definition found.");
                }

                if (!parameter.Values.TryGetValue(environment, out string value))
                {
                    throw new ArgumentException($"Dashboard contains parameter \"{parameterName}\" but definition does not contain value for environment \"{environment}\".");
                }

                JToken newToken = new JValue(value);
                token.Replace(newToken);
            }

            return deparameterizedDashboard;
        }

        private static bool TryGetParameterName(string data, out string parameterName)
        {
            var r = new Regex(@"\[[pP]arameter\((.*)\)\]");
            Match match = r.Match(data);

            if (!match.Success)
            {
                parameterName = null;
                return false;
            }

            parameterName = match.Groups[1].Value;
            return true;
        }
    }

    public class FolderData
    {
        public FolderData(string uid, string title)
        {
            Uid = uid;
            Title = title;
        }

        public string Uid { get; }
        public string Title { get; }

        public int? Id { get; set; }
    }

    public class Parameter
    {
        public string Name { get; set; }
        public IDictionary<string, string> Values { get; set; }
    }
}
