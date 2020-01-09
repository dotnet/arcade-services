// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace DotNet.Grafana
{
    public class GrafanaClient
    {
        private readonly HttpClient _client;
        private readonly ILogger _logger;

        private Credentials _credentials;

        public GrafanaClient(ILogger logger, HttpClient client, string baseUrl)
        {
            _logger = logger;
            _client = client;
            _baseUrl = baseUrl;
        }

        /// <summary>
        /// The base URL to the Grafana instance of interest
        /// </summary>
        /// 
        /// <example>
        /// https://dotnet-eng-grafana.westus2.cloudapp.azure.com/
        /// </example>
        private string _baseUrl;

        public Credentials Credentials
        {
            get => _credentials;
            set
            {
                _credentials = value;
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _credentials.Token);
            }
        }

        public async Task<Health> GetHealthAsync()
        {
            var uri = new Uri(new Uri(_baseUrl), "/api/health");
            Health health = null;

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode)
                {
                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var streamReader = new StreamReader(stream))
                    using (var jsonReader = new JsonTextReader(streamReader))
                    {
                        health = JsonSerializer.CreateDefault().Deserialize<Health>(jsonReader);
                    }
                }
                else
                {
                    _logger.LogWarning("Response does not indicate success");
                }
            }

            return health;
        }

        public async Task<JObject> GetDashboardAsync(string uid)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/dashboards/uid/{uid}");
            JObject dashboard = new JObject();

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode)
                {

                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var streamReader = new StreamReader(stream))
                    using (var jsonReader = new JsonTextReader(streamReader))
                    {
                        dashboard = await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
                    }
                }
                else
                {
                    _logger.LogWarning("Response does not indicate success");
                    _logger.LogWarning("Status {}: {}", response.StatusCode, response.ReasonPhrase);
                    _logger.LogDebug(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }

            return dashboard;
        }

        public async Task<JArray> ListFoldersAsync()
        {
            var uri = new Uri(new Uri(_baseUrl), "/api/folders?limit=50");
            var folder = new JObject();

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
        /// Get a Grafana Folder by its uid
        /// </summary>
        /// <param name="uid">The folder uid</param>
        /// <returns>The Folder JSON object</returns>
        public async Task<JObject> GetFolderAsync(string uid)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/folders/{uid}");
            var folder = new JObject();

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode)
                {

                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var streamReader = new StreamReader(stream))
                    using (var jsonReader = new JsonTextReader(streamReader))
                    {
                        folder = await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
                    }
                }
                else
                {
                    _logger.LogWarning("Response does not indicate success");
                    _logger.LogWarning("Status {}: {}", response.StatusCode, response.ReasonPhrase);
                    _logger.LogDebug(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }

            return folder;
        }

        /// <summary>
        /// Get a Grafana Folder by its id
        /// </summary>
        /// <param name="id">The folder id</param>
        /// <returns>The Folder JSON object</returns>
        public async Task<JObject> GetFolderAsync(int id)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/folders/id/{id}");
            var folder = new JObject();

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode)
                {

                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var streamReader = new StreamReader(stream))
                    using (var jsonReader = new JsonTextReader(streamReader))
                    {
                        folder = await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
                    }
                }
                else
                {
                    _logger.LogWarning("Response does not indicate success");
                    _logger.LogWarning("Status {}: {}", response.StatusCode, response.ReasonPhrase);
                    _logger.LogDebug(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }

            return folder;
        }

        /// <summary>
        /// Get a Data Source by its name
        /// </summary>
        /// <param name="name">The data source name</param>
        /// <returns>The Data Source JSON object as defined by the Grafana Data Source API</returns>
        public async Task<JObject> GetDataSource(string name)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/datasources/name/{name}");
            var folder = new JObject();

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode)
                {

                    using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var streamReader = new StreamReader(stream))
                    using (var jsonReader = new JsonTextReader(streamReader))
                    {
                        folder = await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
                    }
                }
                else
                {
                    _logger.LogWarning("Response does not indicate success");
                    _logger.LogWarning("Status {}: {}", response.StatusCode, response.ReasonPhrase);
                    _logger.LogDebug(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
            }

            return folder;
        }

        public async Task<JObject> CreateFolderAsync(string uid, string title)
        {
            var uri = new Uri(new Uri(_baseUrl), "/api/folders");

            var body = new JObject(
                new JProperty("uid", uid),
                new JProperty("title", title));

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

        public async Task<JObject> CreateDatasourceAsync(JObject datasource)
        {
            var uri = new Uri(new Uri(_baseUrl), "/api/datasources");
            JObject responseJson;

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
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.LogWarning("Response does not indicate success");
                            _logger.LogWarning("Status {}: {}", response.StatusCode, response.ReasonPhrase);
                            _logger.LogDebug(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

                            // TODO: How to handle error?
                        }

                        using (var st = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var sr = new StreamReader(st))
                        using (var jr = new JsonTextReader(sr))
                        {
                            responseJson = await JObject.LoadAsync(jr).ConfigureAwait(false);
                        }
                    }
                }
            }

            return responseJson;
        }

        public async Task<JObject> CreateDashboardAsync(JObject dashboard, int folderId)
        {
            var uri = new Uri(new Uri(_baseUrl), "/api/dashboards/db");

            var dashboardBody = new JObject(
                new JProperty("dashboard", dashboard),
                new JProperty("folderId", folderId),
                new JProperty("overwrite", true));

            JObject responseJson;

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

                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.LogWarning("Response does not indicate success");
                            _logger.LogWarning("Status {}: {}", response.StatusCode, response.ReasonPhrase);
                            _logger.LogDebug(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

                            // TODO: How to handle error?
                        }

                        using (var st = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var sr = new StreamReader(st))
                        using (var jr = new JsonTextReader(sr))
                        {
                            responseJson = await JObject.LoadAsync(jr).ConfigureAwait(false);
                        }
                    }
                }
            }

            return responseJson;
        }
    }

    public class Health
    {
    }
}
