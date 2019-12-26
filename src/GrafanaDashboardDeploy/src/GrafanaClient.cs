// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
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
            BaseUrl = baseUrl;
        }

        /// <summary>
        /// The base URL to the Grafana instance of interest
        /// </summary>
        /// 
        /// <example>
        /// https://dotnet-eng-grafana.westus2.cloudapp.azure.com/
        /// </example>
        public string BaseUrl { get; set; }

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
            var uri = new Uri(new Uri(BaseUrl), "/api/health");
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
            var uri = new Uri(new Uri(BaseUrl), $"/api/dashboards/uid/{uid}");
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

        /// <summary>
        /// Get a Grafana Folder by its uid
        /// </summary>
        /// <param name="uid">The folder uid</param>
        /// <returns>The Folder JSON object</returns>
        public async Task<JObject> GetFolder(string uid)
        {
            var uri = new Uri(new Uri(BaseUrl), $"/api/folders/{uid}");
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
        public async Task<JObject> GetFolder(int id)
        {
            var uri = new Uri(new Uri(BaseUrl), $"/api/folders/id/{id}");
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
            var uri = new Uri(new Uri(BaseUrl), $"/api/datasources/name/{name}");
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
            var uri = new Uri(new Uri(BaseUrl), "/api/folders");

            var body = new JObject(
                new JProperty("uid", uid),
                new JProperty("title", title));

            var folderResponse = new JObject();

            var stream = new MemoryStream();
            using (var textWriter = new StreamWriter(stream))
            using (var jsonStream = new JsonTextWriter(textWriter))
            {
                var serializer = new JsonSerializer();
                serializer.Serialize(jsonStream, body);

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

                        var st = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using (var sr = new StreamReader(st))
                        using (var jr = new JsonTextReader(sr))
                        {
                            folderResponse = await JObject.LoadAsync(jr).ConfigureAwait(false);
                        }
                    }
                }
            }

            return folderResponse;
        }

        public async Task<JObject> CreateDatasourceAsync(JObject datasource)
        {
            var uri = new Uri(new Uri(BaseUrl), "/api/datasources");
            JObject responseJson;

            var stream = new MemoryStream();
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

                        var st = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
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
            var uri = new Uri(new Uri(BaseUrl), "/api/dashboards/db");

            var dashboardBody = new JObject(
                new JProperty("dashboard", dashboard),
                new JProperty("folderId", folderId),
                new JProperty("overwrite", false));

            JObject responseJson;

            var stream = new MemoryStream();
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

                        var st = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
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
}
