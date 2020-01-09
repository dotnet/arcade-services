// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using TaskLoggingHelper = Microsoft.Build.Utilities.TaskLoggingHelper;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public sealed class GrafanaClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly TaskLoggingHelper _logger;
        // e.g. https://dotnet-eng-grafana.westus2.cloudapp.azure.com/
        private readonly string _baseUrl;

        public GrafanaClient(TaskLoggingHelper logger, string baseUrl, string apiToken)
        {
            _logger = logger;
            _baseUrl = baseUrl;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        }

        public async Task<JObject> GetDashboardAsync(string uid)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/dashboards/uid/{uid}");
            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var streamReader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    return await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
                }
            }
        }

        public async Task<JArray> ListFoldersAsync()
        {
            var uri = new Uri(new Uri(_baseUrl), "/api/folders?limit=50");

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var streamReader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    return await JArray.LoadAsync(jsonReader).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Get a Grafana Folder by its id
        /// </summary>
        /// <param name="id">The folder id</param>
        /// <returns>The Folder JSON object</returns>
        public async Task<JObject> GetFolderAsync(int id)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/folders/id/{id}");

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var streamReader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    return await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Get a Data Source by its name
        /// </summary>
        /// <param name="name">The data source name</param>
        /// <returns>The Data Source JSON object as defined by the Grafana Data Source API</returns>
        public async Task<JObject> GetDataSource(string name)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/datasources/name/{name}");

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var streamReader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    return await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
                }
            }
        }

        public async Task<JObject> CreateFolderAsync(string uid, string title)
        {
            var uri = new Uri(new Uri(_baseUrl), "/api/folders");

            var body = new JObject
            {
                {"uid", uid},
                {"title", title},
            };

            using (var content = new StringContent(body.ToString()))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using (HttpResponseMessage response = await _client.PostAsync(uri, content).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    using (Stream responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var sr = new StreamReader(responseStream))
                    using (var jr = new JsonTextReader(sr))
                    {
                        return await JObject.LoadAsync(jr).ConfigureAwait(false);
                    }
                }
            }
        }

        public async Task CreateDatasourceAsync(JObject datasource)
        {
            var uri = new Uri(new Uri(_baseUrl), "/api/datasources");

            using (var stream = new MemoryStream())
            using (var textWriter = new StreamWriter(stream))
            using (var jsonStream = new JsonTextWriter(textWriter))
            {
                var serializer = new JsonSerializer();
                serializer.Serialize(jsonStream, datasource);

                jsonStream.Flush();
                stream.Position = 0;

                using (var content = new StreamContent(stream))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    using (HttpResponseMessage response = await _client.PostAsync(uri, content).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var st = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var sr = new StreamReader(st))
                        using (var jr = new JsonTextReader(sr))
                        {
                            await JObject.LoadAsync(jr).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        public async Task CreateDashboardAsync(JObject dashboard, int folderId)
        {
            var uri = new Uri(new Uri(_baseUrl), "/api/dashboards/db");

            var dashboardBody = new JObject
            {
                {"dashboard", dashboard},
                {"folderId", folderId},
                {"overwrite", true},
            };

            using (var stream = new MemoryStream())
            using (var textWriter = new StreamWriter(stream))
            using (var jsonStream = new JsonTextWriter(textWriter))
            {
                var serializer = new JsonSerializer();
                serializer.Serialize(jsonStream, dashboardBody);

                jsonStream.Flush();
                stream.Position = 0;

                using (var content = new StreamContent(stream))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    using (HttpResponseMessage response = await _client.PostAsync(uri, content).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var st = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var sr = new StreamReader(st))
                        using (var jr = new JsonTextReader(sr))
                        {
                            await JObject.LoadAsync(jr).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
