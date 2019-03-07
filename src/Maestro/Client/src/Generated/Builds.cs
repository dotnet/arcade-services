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
        Task<IImmutableList<Build>> ListBuildsAsync(
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

        public async Task<IImmutableList<Build>> ListBuildsAsync(
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
                return _res.Body;
            }
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
            if (channelId != default)
            {
                _query.Add("channelId", Client.Serialize(channelId));
            }
            if (notBefore != default)
            {
                _query.Add("notBefore", Client.Serialize(notBefore));
            }
            if (notAfter != default)
            {
                _query.Add("notAfter", Client.Serialize(notAfter));
            }
            if (loadCollections != default)
            {
                _query.Add("loadCollections", Client.Serialize(loadCollections));
            }
            if (page != default)
            {
                _query.Add("page", Client.Serialize(page));
            }
            if (perPage != default)
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
                string _responseContent;
                if (!_res.IsSuccessStatusCode)
                {
                    _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var ex = new RestApiException<ApiError>(
                        new HttpRequestMessageWrapper(_req, null),
                        new HttpResponseMessageWrapper(_res, _responseContent),
                        Client.Deserialize<ApiError>(_responseContent)
);
                    HandleFailedListBuildsRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
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

        internal async Task<HttpOperationResponse<Build>> CreateInternalAsync(
            BuildData body,
            CancellationToken cancellationToken = default
        )
        {
            if (body == default)
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
                if (body != default)
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
                string _responseContent;
                if (!_res.IsSuccessStatusCode)
                {
                    _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var ex = new RestApiException<ApiError>(
                        new HttpRequestMessageWrapper(_req, _requestContent),
                        new HttpResponseMessageWrapper(_res, _responseContent),
                        Client.Deserialize<ApiError>(_responseContent)
);
                    HandleFailedCreateRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
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

        internal async Task<HttpOperationResponse<Build>> GetBuildInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default)
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
                string _responseContent;
                if (!_res.IsSuccessStatusCode)
                {
                    _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var ex = new RestApiException<ApiError>(
                        new HttpRequestMessageWrapper(_req, null),
                        new HttpResponseMessageWrapper(_res, _responseContent),
                        Client.Deserialize<ApiError>(_responseContent)
);
                    HandleFailedGetBuildRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
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

        internal async Task<HttpOperationResponse<BuildGraph>> GetBuildGraphInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default)
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/builds/{id}/tree";
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
                string _responseContent;
                if (!_res.IsSuccessStatusCode)
                {
                    _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var ex = new RestApiException<ApiError>(
                        new HttpRequestMessageWrapper(_req, null),
                        new HttpResponseMessageWrapper(_res, _responseContent),
                        Client.Deserialize<ApiError>(_responseContent)
);
                    HandleFailedGetBuildGraphRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
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
            if (channelId != default)
            {
                _query.Add("channelId", Client.Serialize(channelId));
            }
            if (notBefore != default)
            {
                _query.Add("notBefore", Client.Serialize(notBefore));
            }
            if (notAfter != default)
            {
                _query.Add("notAfter", Client.Serialize(notAfter));
            }
            if (loadCollections != default)
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
                string _responseContent;
                if (!_res.IsSuccessStatusCode)
                {
                    _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var ex = new RestApiException<ApiError>(
                        new HttpRequestMessageWrapper(_req, null),
                        new HttpResponseMessageWrapper(_res, _responseContent),
                        Client.Deserialize<ApiError>(_responseContent)
);
                    HandleFailedGetLatestRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
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
