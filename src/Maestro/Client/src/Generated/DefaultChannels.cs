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
    public partial interface IDefaultChannels
    {
        Task<IImmutableList<DefaultChannel>> ListAsync(
            string branch = default,
            int? channelId = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task CreateAsync(
            PostData body,
            CancellationToken cancellationToken = default
        );

        Task<DefaultChannel> GetAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task DeleteAsync(
            int id,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class DefaultChannels : IServiceOperations<MaestroApi>, IDefaultChannels
    {
        public DefaultChannels(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListRequest(RestApiException ex);

        public async Task<IImmutableList<DefaultChannel>> ListAsync(
            string branch = default,
            int? channelId = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await ListInternalAsync(
                branch,
                channelId,
                repository,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task<HttpOperationResponse<IImmutableList<DefaultChannel>>> ListInternalAsync(
            string branch = default,
            int? channelId = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            const string apiVersion = "2019-01-16";

            var _path = "/api/default-channels";

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(repository))
            {
                _query.Add("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _query.Add("branch", Client.Serialize(branch));
            }
            if (channelId != default)
            {
                _query.Add("channelId", Client.Serialize(channelId));
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
                    HandleFailedListRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<IImmutableList<DefaultChannel>>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<IImmutableList<DefaultChannel>>(_responseContent),
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

        public async Task CreateAsync(
            PostData body,
            CancellationToken cancellationToken = default
        )
        {
            using (await CreateInternalAsync(
                body,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return;
            }
        }

        internal async Task<HttpOperationResponse> CreateInternalAsync(
            PostData body,
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

            var _path = "/api/default-channels";

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

        partial void HandleFailedGetRequest(RestApiException ex);

        public async Task<DefaultChannel> GetAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetInternalAsync(
                id,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task<HttpOperationResponse<DefaultChannel>> GetInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default)
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/default-channels/{id}";
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
                    HandleFailedGetRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<DefaultChannel>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<DefaultChannel>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedDeleteRequest(RestApiException ex);

        public async Task DeleteAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            using (await DeleteInternalAsync(
                id,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return;
            }
        }

        internal async Task<HttpOperationResponse> DeleteInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default)
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/default-channels/{id}";
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
                _req = new HttpRequestMessage(HttpMethod.Delete, _url);

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
                    HandleFailedDeleteRequest(ex);
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
