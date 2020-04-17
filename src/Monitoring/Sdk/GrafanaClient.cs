// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.Monitoring.Sdk
{
    public sealed class GrafanaClient : IDisposable
    {
        private readonly HttpClient _client;

        // e.g. https://dotnet-eng-grafana.westus2.cloudapp.azure.com/
        private readonly string _baseUrl;

        public GrafanaClient(string baseUrl, string apiToken)
        {
            _baseUrl = baseUrl;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        }

        public async Task<JObject> GetDashboardAsync(string uid)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/dashboards/uid/{uid}");
            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                await response.EnsureSuccessWithContentAsync();

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
                await response.EnsureSuccessWithContentAsync();

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
                await response.EnsureSuccessWithContentAsync();

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
        public async Task<JObject> GetDataSourceAsync(string name)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/datasources/name/{name}");

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                await response.EnsureSuccessWithContentAsync();

                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var streamReader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    return await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
                }
            }
        }

        public Task<JObject> CreateFolderAsync(string uid, string title)
        {
            var folder = new JObject
            {
                {"uid", uid},
                {"title", title},
            };

            return CreateOrUpdateAsync(
                folder,
                folder.Value<string>("uid"),
                u => $"/api/folders/{Uri.EscapeDataString(u)}",
                "/api/folders",
                _ => (HttpMethod.Put, $"/api/folders/{uid}"),
                (d, x) =>
                {
                    d.Remove("uid");
                    d["version"] = x.Value<int>("version");
                }
            );
        }
        
        public Task CreateDatasourceAsync(JObject datasource)
        {
            return CreateOrUpdateAsync(
                datasource,
                datasource.Value<string>("name"),
                n => $"/api/datasources/name/{Uri.EscapeDataString(n)}",
                "/api/datasources",
                x => (HttpMethod.Put, $"/api/datasources/{x.Value<int>("id")}"),
                (d, x) =>
                {
                    d["id"] = x.Value<int>("id");
                    d["version"] = x.Value<int>("version");
                }
            );
        }

        public Task CreateNotificationChannelAsync(JObject notificationChannel)
        {
            return CreateOrUpdateAsync(
                notificationChannel,
                notificationChannel.Value<string>("uid"),
                uid => $"/api/alert-notifications/uid/{Uri.EscapeDataString(uid)}",
                "/api/alert-notifications",
                x => (HttpMethod.Put, $"/api/alert-notifications/{x.Value<int>("id")}"),
                (d, x) =>
                {
                    d["id"] = x.Value<int>("id");
                    d["uid"] = x.Value<string>("uid");
                    d["version"] = x.Value<int>("version");
                }
            );
        }

        private async Task<JObject> CreateOrUpdateAsync<TExternalId>(
            JObject data,
            TExternalId id,
            Func<TExternalId, string> getUrl,
            string createUrl,
            Func<JObject, (HttpMethod method, string url)> updateUrl,
            Action<JObject, JObject> updateState)
        {
            using (var exist = await _client.GetAsync(new Uri(new Uri(_baseUrl), getUrl(id))).ConfigureAwait(false))
            {
                if (exist.StatusCode == HttpStatusCode.NotFound)
                {
                    return await SendObjectAsync(data, new Uri(new Uri(_baseUrl), createUrl)).ConfigureAwait(false);
                }
                
                await exist.EnsureSuccessWithContentAsync();

                (HttpMethod method, string url) updateInfo;
                using (var st = await exist.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var sr = new StreamReader(st))
                using (var jr = new JsonTextReader(sr))
                {
                    JObject existingData = await JObject.LoadAsync(jr).ConfigureAwait(false);
                    updateState(data, existingData);
                    updateInfo = updateUrl(existingData);
                }


                return await SendObjectAsync(data, new Uri(new Uri(_baseUrl), updateInfo.url), updateInfo.method)
                    .ConfigureAwait(false);
            }
        }

        private async Task<JObject> SendObjectAsync(JObject value, Uri uri, HttpMethod method = null)
        {
            method = method ?? HttpMethod.Post;
            using (var stream = new MemoryStream())
            using (var textWriter = new StreamWriter(stream))
            using (var jsonStream = new JsonTextWriter(textWriter))
            {
                var serializer = new JsonSerializer();
                serializer.Serialize(jsonStream, value);

                jsonStream.Flush();
                stream.Position = 0;

                using (var content = new StreamContent(stream))
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    using (var request = new HttpRequestMessage(method, uri) {Content = content})
                    using (HttpResponseMessage response = await _client.SendAsync(request).ConfigureAwait(false))
                    {
                        await response.EnsureSuccessWithContentAsync();

                        using (var st = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var sr = new StreamReader(st))
                        using (var jr = new JsonTextReader(sr))
                        {
                            return await JObject.LoadAsync(jr).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        public Task CreateDashboardAsync(JObject dashboard, int folderId)
        {
            var dashboardBody = new JObject
            {
                {"dashboard", dashboard},
                {"folderId", folderId},
                {"overwrite", true},
            };

            return CreateOrUpdateAsync(
                dashboardBody,
                dashboardBody.Value<JObject>("dashboard").Value<string>("uid"),
                u => $"/api/dashboards/uid/{Uri.EscapeDataString(u)}",
                "/api/dashboards/db",
                _ => (HttpMethod.Post, "/api/dashboards/db"),
                (d, x) =>
                {
                    var dObj = d.Value<JObject>("dashboard");
                    var xObj = d.Value<JObject>("dashboard");
                    dObj["id"] = xObj.Value<int>("id");
                    dObj["version"] = xObj.Value<int>("version");
                }
            );
        }

        public async Task<JArray> SearchDashboardsByTagAsync(string tag)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/search?tag={Uri.EscapeDataString(tag)}");

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                await response.EnsureSuccessWithContentAsync();

                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var streamReader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    return await JArray.LoadAsync(jsonReader).ConfigureAwait(false);
                }
            }
        }

        public async Task DeleteDashboardAsync(string uid)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/dashboards/uid/{Uri.EscapeDataString(uid)}");

            using (HttpResponseMessage response = await _client.DeleteAsync(uri).ConfigureAwait(false))
            {
                await response.EnsureSuccessWithContentAsync();
            }
        }

        public async Task<JObject> GetNotificationChannelAsync(string uid)
        {
            var uri = new Uri(new Uri(_baseUrl), $"/api/alert-notifications/uid/{Uri.EscapeDataString(uid)}");

            using (HttpResponseMessage response = await _client.GetAsync(uri).ConfigureAwait(false))
            {
                await response.EnsureSuccessWithContentAsync();

                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var streamReader = new StreamReader(stream))
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    return await JObject.LoadAsync(jsonReader).ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
