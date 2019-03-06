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
    public partial interface IRepository
    {
        Task<IImmutableList<MergePolicy>> GetMergePoliciesAsync(
            string branch,
            string repository,
            CancellationToken cancellationToken = default
        );

        Task SetMergePoliciesAsync(
            string branch,
            string repository,
            IImmutableList<MergePolicy> body = default,
            CancellationToken cancellationToken = default
        );

        Task<IImmutableList<RepositoryHistoryItem>> GetHistoryAsync(
            string branch = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task RetryActionAsyncAsync(
            string branch,
            string repository,
            long timestamp,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Repository : IServiceOperations<MaestroApi>, IRepository
    {
        public Repository(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedGetMergePoliciesRequest(RestApiException ex);

        public async Task<IImmutableList<MergePolicy>> GetMergePoliciesAsync(
            string branch,
            string repository,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetMergePoliciesInternalAsync(
                branch,
                repository,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task<HttpOperationResponse<IImmutableList<MergePolicy>>> GetMergePoliciesInternalAsync(
            string branch,
            string repository,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(repository))
            {
                throw new ArgumentNullException(nameof(repository));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/repo-config/merge-policy";

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(repository))
            {
                _query.Add("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _query.Add("branch", Client.Serialize(branch));
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
                    HandleFailedGetMergePoliciesRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<IImmutableList<MergePolicy>>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<IImmutableList<MergePolicy>>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedSetMergePoliciesRequest(RestApiException ex);

        public async Task SetMergePoliciesAsync(
            string branch,
            string repository,
            IImmutableList<MergePolicy> body = default,
            CancellationToken cancellationToken = default
        )
        {
            using (await SetMergePoliciesInternalAsync(
                branch,
                repository,
                body,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return;
            }
        }

        internal async Task<HttpOperationResponse> SetMergePoliciesInternalAsync(
            string branch,
            string repository,
            IImmutableList<MergePolicy> body = default,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(repository))
            {
                throw new ArgumentNullException(nameof(repository));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/repo-config/merge-policy";

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(repository))
            {
                _query.Add("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _query.Add("branch", Client.Serialize(branch));
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
                    HandleFailedSetMergePoliciesRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse
                {
                    Request = _req,
                    Response = _res,
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedGetHistoryRequest(RestApiException ex);

        public async Task<IImmutableList<RepositoryHistoryItem>> GetHistoryAsync(
            string branch = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetHistoryInternalAsync(
                branch,
                page,
                perPage,
                repository,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task<HttpOperationResponse<IImmutableList<RepositoryHistoryItem>>> GetHistoryInternalAsync(
            string branch = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            const string apiVersion = "2019-01-16";

            var _path = "/api/repo-config/history";

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(repository))
            {
                _query.Add("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _query.Add("branch", Client.Serialize(branch));
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
                    HandleFailedGetHistoryRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<IImmutableList<RepositoryHistoryItem>>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<IImmutableList<RepositoryHistoryItem>>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedRetryActionAsyncRequest(RestApiException ex);

        public async Task RetryActionAsyncAsync(
            string branch,
            string repository,
            long timestamp,
            CancellationToken cancellationToken = default
        )
        {
            using (await RetryActionAsyncInternalAsync(
                branch,
                repository,
                timestamp,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return;
            }
        }

        internal async Task<HttpOperationResponse> RetryActionAsyncInternalAsync(
            string branch,
            string repository,
            long timestamp,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(repository))
            {
                throw new ArgumentNullException(nameof(repository));
            }

            if (timestamp == default)
            {
                throw new ArgumentNullException(nameof(timestamp));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/repo-config/retry/{timestamp}";
            _path = _path.Replace("{timestamp}", Client.Serialize(timestamp));

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(repository))
            {
                _query.Add("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _query.Add("branch", Client.Serialize(branch));
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
                _req = new HttpRequestMessage(HttpMethod.Post, _url);

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
                    HandleFailedRetryActionAsyncRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse
                {
                    Request = _req,
                    Response = _res,
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
