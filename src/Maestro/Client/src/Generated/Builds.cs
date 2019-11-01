using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Rest;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Microsoft.DotNet.Maestro.Client
{
    public partial interface IBuilds
    {
        Task<PagedResponse<Build>> ListBuildsAsync(
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task<Build> CreateAsync(
            BuildData body,
            CancellationToken cancellationToken = default
        );

        Task<Build> GetBuildAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<BuildGraph> GetBuildGraphAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<Build> GetLatestAsync(
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task<Build> UpdateAsync(
            BuildUpdate body,
            int buildId,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Builds : IServiceOperations<MaestroApi>, IBuilds
    {
        public Builds(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListBuildsRequest(RestApiException ex);

        public async Task<PagedResponse<Build>> ListBuildsAsync(
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await ListBuildsInternalAsync(
                buildNumber,
                channelId,
                commit,
                loadCollections,
                notAfter,
                notBefore,
                page,
                perPage,
                repository,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return new PagedResponse<Build>(Client, OnListBuildsFailed, _res);
            }
        }

        internal async Task OnListBuildsFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedListBuildsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<IImmutableList<Build>>> ListBuildsInternalAsync(
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            const string apiVersion = "2019-01-16";

            var _path = "/api/builds";

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(repository))
            {
                _query.Add("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(commit))
            {
                _query.Add("commit", Client.Serialize(commit));
            }
            if (!string.IsNullOrEmpty(buildNumber))
            {
                _query.Add("buildNumber", Client.Serialize(buildNumber));
            }
            if (channelId != default(int?))
            {
                _query.Add("channelId", Client.Serialize(channelId));
            }
            if (notBefore != default(DateTimeOffset?))
            {
                _query.Add("notBefore", Client.Serialize(notBefore));
            }
            if (notAfter != default(DateTimeOffset?))
            {
                _query.Add("notAfter", Client.Serialize(notAfter));
            }
            if (loadCollections != default(bool?))
            {
                _query.Add("loadCollections", Client.Serialize(loadCollections));
            }
            if (page != default(int?))
            {
                _query.Add("page", Client.Serialize(page));
            }
            if (perPage != default(int?))
            {
                _query.Add("perPage", Client.Serialize(perPage));
            }
            _query.Add("api-version", Client.Serialize(apiVersion));

            var _uriBuilder = new UriBuilder(Client.BaseUri);
            _uriBuilder.Path = _uriBuilder.Path.TrimEnd('/') + _path;
            _uriBuilder.Query = _query.ToString();
            var _url = _uriBuilder.Uri;

            HttpRequestMessage _req = null;
            HttpResponseMessage _res = null;
            try
            {
                _req = new HttpRequestMessage(HttpMethod.Get, _url);

                if (Client.Credentials != null)
                {
                    await Client.Credentials.ProcessHttpRequestAsync(_req, cancellationToken).ConfigureAwait(false);
                }

                _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false);
                if (!_res.IsSuccessStatusCode)
                {
                    await OnListBuildsFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<IImmutableList<Build>>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<IImmutableList<Build>>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedCreateRequest(RestApiException ex);

        public async Task<Build> CreateAsync(
            BuildData body,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await CreateInternalAsync(
                body,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnCreateFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, content),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedCreateRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<Build>> CreateInternalAsync(
            BuildData body,
            CancellationToken cancellationToken = default
        )
        {
            if (body == default(BuildData))
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (!body.IsValid)
            {
                throw new ArgumentException("The parameter is not valid", nameof(body));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/builds";

            var _query = new QueryBuilder();
            _query.Add("api-version", Client.Serialize(apiVersion));

            var _uriBuilder = new UriBuilder(Client.BaseUri);
            _uriBuilder.Path = _uriBuilder.Path.TrimEnd('/') + _path;
            _uriBuilder.Query = _query.ToString();
            var _url = _uriBuilder.Uri;

            HttpRequestMessage _req = null;
            HttpResponseMessage _res = null;
            try
            {
                _req = new HttpRequestMessage(HttpMethod.Post, _url);

                string _requestContent = null;
                if (body != default(BuildData))
                {
                    _requestContent = Client.Serialize(body);
                    _req.Content = new StringContent(_requestContent, Encoding.UTF8)
                    {
                        Headers =
                        {
                            ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8"),
                        },
                    };
                }

                if (Client.Credentials != null)
                {
                    await Client.Credentials.ProcessHttpRequestAsync(_req, cancellationToken).ConfigureAwait(false);
                }

                _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false);
                if (!_res.IsSuccessStatusCode)
                {
                    await OnCreateFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<Build>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<Build>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedGetBuildRequest(RestApiException ex);

        public async Task<Build> GetBuildAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetBuildInternalAsync(
                id,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnGetBuildFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedGetBuildRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<Build>> GetBuildInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default(int))
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/builds/{id}";
            _path = _path.Replace("{id}", Client.Serialize(id));

            var _query = new QueryBuilder();
            _query.Add("api-version", Client.Serialize(apiVersion));

            var _uriBuilder = new UriBuilder(Client.BaseUri);
            _uriBuilder.Path = _uriBuilder.Path.TrimEnd('/') + _path;
            _uriBuilder.Query = _query.ToString();
            var _url = _uriBuilder.Uri;

            HttpRequestMessage _req = null;
            HttpResponseMessage _res = null;
            try
            {
                _req = new HttpRequestMessage(HttpMethod.Get, _url);

                if (Client.Credentials != null)
                {
                    await Client.Credentials.ProcessHttpRequestAsync(_req, cancellationToken).ConfigureAwait(false);
                }

                _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false);
                if (!_res.IsSuccessStatusCode)
                {
                    await OnGetBuildFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<Build>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<Build>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedGetBuildGraphRequest(RestApiException ex);

        public async Task<BuildGraph> GetBuildGraphAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetBuildGraphInternalAsync(
                id,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnGetBuildGraphFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedGetBuildGraphRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<BuildGraph>> GetBuildGraphInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default(int))
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/builds/{id}/graph";
            _path = _path.Replace("{id}", Client.Serialize(id));

            var _query = new QueryBuilder();
            _query.Add("api-version", Client.Serialize(apiVersion));

            var _uriBuilder = new UriBuilder(Client.BaseUri);
            _uriBuilder.Path = _uriBuilder.Path.TrimEnd('/') + _path;
            _uriBuilder.Query = _query.ToString();
            var _url = _uriBuilder.Uri;

            HttpRequestMessage _req = null;
            HttpResponseMessage _res = null;
            try
            {
                _req = new HttpRequestMessage(HttpMethod.Get, _url);

                if (Client.Credentials != null)
                {
                    await Client.Credentials.ProcessHttpRequestAsync(_req, cancellationToken).ConfigureAwait(false);
                }

                _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false);
                if (!_res.IsSuccessStatusCode)
                {
                    await OnGetBuildGraphFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<BuildGraph>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<BuildGraph>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedGetLatestRequest(RestApiException ex);

        public async Task<Build> GetLatestAsync(
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetLatestInternalAsync(
                buildNumber,
                channelId,
                commit,
                loadCollections,
                notAfter,
                notBefore,
                repository,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnGetLatestFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedGetLatestRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<Build>> GetLatestInternalAsync(
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            const string apiVersion = "2019-01-16";

            var _path = "/api/builds/latest";

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(repository))
            {
                _query.Add("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(commit))
            {
                _query.Add("commit", Client.Serialize(commit));
            }
            if (!string.IsNullOrEmpty(buildNumber))
            {
                _query.Add("buildNumber", Client.Serialize(buildNumber));
            }
            if (channelId != default(int?))
            {
                _query.Add("channelId", Client.Serialize(channelId));
            }
            if (notBefore != default(DateTimeOffset?))
            {
                _query.Add("notBefore", Client.Serialize(notBefore));
            }
            if (notAfter != default(DateTimeOffset?))
            {
                _query.Add("notAfter", Client.Serialize(notAfter));
            }
            if (loadCollections != default(bool?))
            {
                _query.Add("loadCollections", Client.Serialize(loadCollections));
            }
            _query.Add("api-version", Client.Serialize(apiVersion));

            var _uriBuilder = new UriBuilder(Client.BaseUri);
            _uriBuilder.Path = _uriBuilder.Path.TrimEnd('/') + _path;
            _uriBuilder.Query = _query.ToString();
            var _url = _uriBuilder.Uri;

            HttpRequestMessage _req = null;
            HttpResponseMessage _res = null;
            try
            {
                _req = new HttpRequestMessage(HttpMethod.Get, _url);

                if (Client.Credentials != null)
                {
                    await Client.Credentials.ProcessHttpRequestAsync(_req, cancellationToken).ConfigureAwait(false);
                }

                _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false);
                if (!_res.IsSuccessStatusCode)
                {
                    await OnGetLatestFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<Build>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<Build>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedUpdateRequest(RestApiException ex);

        public async Task<Build> UpdateAsync(
            BuildUpdate body,
            int buildId,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await UpdateInternalAsync(
                body,
                buildId,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnUpdateFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, content),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedUpdateRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<Build>> UpdateInternalAsync(
            BuildUpdate body,
            int buildId,
            CancellationToken cancellationToken = default
        )
        {
            if (body == default(BuildUpdate))
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (buildId == default(int))
            {
                throw new ArgumentNullException(nameof(buildId));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/builds/{buildId}";
            _path = _path.Replace("{buildId}", Client.Serialize(buildId));

            var _query = new QueryBuilder();
            _query.Add("api-version", Client.Serialize(apiVersion));

            var _uriBuilder = new UriBuilder(Client.BaseUri);
            _uriBuilder.Path = _uriBuilder.Path.TrimEnd('/') + _path;
            _uriBuilder.Query = _query.ToString();
            var _url = _uriBuilder.Uri;

            HttpRequestMessage _req = null;
            HttpResponseMessage _res = null;
            try
            {
                _req = new HttpRequestMessage(new HttpMethod("PATCH"), _url);

                string _requestContent = null;
                if (body != default(BuildUpdate))
                {
                    _requestContent = Client.Serialize(body);
                    _req.Content = new StringContent(_requestContent, Encoding.UTF8)
                    {
                        Headers =
                        {
                            ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8"),
                        },
                    };
                }

                if (Client.Credentials != null)
                {
                    await Client.Credentials.ProcessHttpRequestAsync(_req, cancellationToken).ConfigureAwait(false);
                }

                _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false);
                if (!_res.IsSuccessStatusCode)
                {
                    await OnUpdateFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<Build>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<Build>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }
    }
}
