using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public class MockTimeoutAzureClient : IAzureDevOpsClient
    {
        public IDictionary<Build, List<Timeline>> Builds { get; }

        private HttpClient _httpClient;

        public MockTimeoutAzureClient(Dictionary<Build, List<Timeline>> builds, HttpMessageHandler httpMessageHandler)
        {
            Builds = builds;
            _httpClient = new HttpClient(httpMessageHandler);
        }

        public Task<WorkItem> CreateRcaWorkItem(string project, string title, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Build> GetBuildAsync(string project, long buildId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<(BuildChange[] changes, int truncatedChangeCount)> GetBuildChangesAsync(string project, long buildId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<BuildChangeDetail> GetChangeDetails(string changeUrl, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Timeline> GetTimelineAsync(string project, int buildId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Builds
                .Single(build => build.Key.Id == buildId && build.Key.Project.Name == project)
                .Value.OrderByDescending(timeline => timeline.LastChangedOn).First());
        }

        public Task<Timeline> GetTimelineAsync(string project, int buildId, string timelineId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Builds
                .Single(build => build.Key.Id == buildId && build.Key.Project.Name == project)
                .Value.Single(timeline => timeline.Id == timelineId));
        }

        public Task<Build[]> ListBuilds(string project, CancellationToken cancellationToken, DateTimeOffset? minTime = null, int? limit = null)
        {
            return Task.FromResult(Builds.Keys.ToArray());
        }

        public Task<AzureDevOpsProject[]> ListProjectsAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<string> TryGetImageName(string logUri, Func<string, string> regexFunction, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, logUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            response.EnsureSuccessStatusCode();
                
            return await response.Content.ReadAsStringAsync();
        }
    }
}
