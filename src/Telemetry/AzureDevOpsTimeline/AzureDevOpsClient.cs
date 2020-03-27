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
            int? limit,
            CancellationToken cancellationToken)
        {
            StringBuilder builder = GetProjectApiRootBuilder(project);
            builder.Append("/build/builds?");
            builder.Append($"continuationToken={continuationToken}&");
            builder.Append("queryOrder=finishTimeAscending&");
            
            if (minTime.HasValue)
            {
                builder.Append($"minTime={minTime.Value.UtcDateTime:O}&");
            }

            if (limit.HasValue)
            {
                builder.Append($"$top={limit}&");
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
            DateTimeOffset? minTime = default,
            int? limit = default)
        {
            var buildList = new List<Build>();
            string continuationToken = null;
            do
            {
                JsonResult result = await ListBuildsRaw(project, continuationToken, minTime, limit, cancellationToken);
                continuationToken = result.ContinuationToken;
                JObject root = JObject.Parse(result.Body);
                var array = (JArray) root["value"];
                var builds = array.ToObject<Build[]>();
                buildList.AddRange(builds);
            } while (continuationToken != null && (!limit.HasValue || buildList.Count < limit.Value));

            return buildList.ToArray();
        }

        public async Task<AzureDevOpsProject[]> ListProjects(CancellationToken cancellationToken = default)
        {
            JsonResult result = await GetJsonResult($"{_baseUrl}/{_organization}/_apis/projects?api-version=5.1", cancellationToken);
            return JsonConvert.DeserializeObject<AzureDevOpsArrayOf<AzureDevOpsProject>>(result.Body).Value;
        }

        public async Task<Build> GetBuild(string project, long buildId, CancellationToken cancellationToken = default)
        {
            StringBuilder builder = GetProjectApiRootBuilder(project);
            builder.Append($"/build/builds/{buildId}?api-version=5.1");
            JsonResult jsonResult = await GetJsonResult(builder.ToString(), cancellationToken);
            return JsonConvert.DeserializeObject<Build>(jsonResult.Body);
        }
        public async Task<(BuildChanges[] changes, int more)> GetBuildChangesAsync(string project, long buildId, CancellationToken cancellationToken = default)
        {
            StringBuilder builder = GetProjectApiRootBuilder(project);
            builder.Append($"/build/builds/{buildId}/changes?$top=10&api-version=5.1");
            JsonResult jsonResult = await GetJsonResult(builder.ToString(), cancellationToken);
            var arrayOf = JsonConvert.DeserializeObject<AzureDevOpsArrayOf<BuildChanges>>(jsonResult.Body);
            return (arrayOf.Value, arrayOf.Count - arrayOf.Value.Length);
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
                int retry = 5;
                while (true)
                {
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
                    catch (OperationCanceledException e) when (e.CancellationToken == cancellationToken)
                    {
                        throw;
                    }
                    catch (Exception) when (retry -- > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    }
                }
            }
            finally
            {
                _parallelism.Release();
            }
        }

        public async Task<BuildChangeDetail> GetChangeDetails(string changeUrl, CancellationToken cancellationToken = default)
        {
            var result = await GetJsonResult(changeUrl, cancellationToken);
            return JsonConvert.DeserializeObject<BuildChangeDetail>(result.Body);
        }
    }

    public class BuildChangeDetail
    {
        [JsonProperty("_links")]
        public BuildLinks Links { get; set; }
    }

    public class BuildChanges
    {
        public BuildChanges(string id, IdentityRef author, string message, string type, string displayUri, string location)
        {
            Id = id;
            Author = author;
            Message = message;
            Type = type;
            DisplayUri = displayUri;
            Location = location;
        }

        public string Id { get;  }
        public IdentityRef Author { get; }
        public string Message { get; }

        public string Type { get; }
        public string DisplayUri { get; }
        public string Location { get; }
    }

    public class AzureDevOpsArrayOf<T>
    {
        public AzureDevOpsArrayOf(int count, T[] value)
        {
            Count = count;
            Value = value;
        }

        public int Count { get; }
        public T[] Value { get; }
    }

    public class AzureDevOpsProject
    {
        public AzureDevOpsProject(string id, string name, string description, string url, string state, int revision, string visibility)
        {
            Id = id;
            Name = name;
            Description = description;
            Url = url;
            State = state;
            Revision = revision;
            Visibility = visibility;
        }

        public string Id { get; }
        public string Name { get;}
        public string Description { get; }
        public string Url { get; }
        public string State { get; }
        public int Revision { get; }
        public string Visibility { get; }

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
