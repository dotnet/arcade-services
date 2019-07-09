// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.DotNet.AzureDevOpsTimeline
{
    public sealed class AzureDevOpsClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _organization;
        private readonly SemaphoreSlim _parallelism;

        public AzureDevOpsClient(
            string baseUrl,
            string organization,
            int maxParallelRequests,
            string accessToken)
        {
            _baseUrl = baseUrl;
            _organization = organization;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _parallelism = new SemaphoreSlim(maxParallelRequests, maxParallelRequests);

            if (!string.IsNullOrEmpty(accessToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes($":{accessToken}"))
                );
            }
        }

        /// <summary>
        ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0
        /// </summary>
        private async Task<JsonResult> ListBuildsRaw(
            string project,
            string continuationToken,
            DateTimeOffset? minTime,
            CancellationToken cancellationToken)
        {
            StringBuilder builder = GetProjectApiRootBuilder(project);
            builder.Append("/build/builds?");
            builder.Append($"continuationToken={continuationToken}&");
            builder.Append("queryOrder=finishTimeDescending&");

            if (minTime.HasValue)
            {
                builder.Append($"minTime={minTime.Value.UtcDateTime:O}&");
            }

            builder.Append("statusFilter=completed&");
            builder.Append("api-version=5.0");
            return await GetJsonResult(builder.ToString(), cancellationToken);
        }

        /// <summary>
        ///     https://docs.microsoft.com/en-us/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.0
        /// </summary>
        public async Task<Build[]> ListBuilds(
            string project,
            CancellationToken cancellationToken,
            DateTimeOffset? minTime = default)
        {
            var buildList = new List<Build>();
            string continuationToken = null;
            do
            {
                JsonResult result = await ListBuildsRaw(project, continuationToken, minTime, cancellationToken);
                continuationToken = result.ContinuationToken;
                JObject root = JObject.Parse(result.Body);
                var array = (JArray) root["value"];
                var builds = array.ToObject<Build[]>();
                buildList.AddRange(builds);
            } while (continuationToken != null);

            return buildList.ToArray();
        }

        private async Task<string> GetTimelineRaw(string project, int buildId, CancellationToken cancellationToken)
        {
            StringBuilder builder = GetProjectApiRootBuilder(project);
            builder.Append($"/build/builds/{buildId}/timeline?api-version=5.0");
            return (await GetJsonResult(builder.ToString(), cancellationToken)).Body;
        }

        public async Task<Timeline> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken)
        {
            string json = await GetTimelineRaw(project, buildId, cancellationToken);
            return JsonConvert.DeserializeObject<Timeline>(json);
        }

        private StringBuilder GetProjectApiRootBuilder(string project)
        {
            var builder = new StringBuilder();
            builder.Append($"{_baseUrl}/{_organization}/{project}/_apis");
            return builder;
        }

        private async Task<JsonResult> GetJsonResult(string uri, CancellationToken cancellationToken)
        {
            await _parallelism.WaitAsync(cancellationToken);
            try
            {
                using (HttpResponseMessage response = await _httpClient.GetAsync(uri, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    string responseBody = await response.Content.ReadAsStringAsync();
                    response.Headers.TryGetValues("x-ms-continuationtoken",
                        out IEnumerable<string> continuationTokenHeaders);
                    string continuationToken = continuationTokenHeaders?.FirstOrDefault();
                    var result = new JsonResult(responseBody, continuationToken);

                    return result;
                }
            }
            finally
            {
                _parallelism.Release();
            }
        }
    }

    public sealed class JsonResult
    {
        public JsonResult(string body, string continuationToken)
        {
            Body = body;
            ContinuationToken = continuationToken;
        }

        public string Body { get; }
        public string ContinuationToken { get; }
    }
}
