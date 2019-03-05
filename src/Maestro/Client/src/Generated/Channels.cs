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
    public partial interface IChannels
    {
        Task<IImmutableList<Channel>> ListChannelsAsync(
            string classification = default,
            CancellationToken cancellationToken = default
        );

        Task<Channel> CreateChannelAsync(
            string classification,
            string name,
            CancellationToken cancellationToken = default
        );

        Task<Channel> GetChannelAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<Channel> DeleteChannelAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task AddBuildToChannelAsync(
            int buildId,
            int channelId,
            CancellationToken cancellationToken = default
        );

        Task AddPipelineToChannelAsync(
            int channelId,
            int pipelineId,
            CancellationToken cancellationToken = default
        );

        Task DeletePipelineFromChannelAsync(
            int channelId,
            int pipelineId,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Channels : IServiceOperations<MaestroApi>, IChannels
    {
        public Channels(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListChannelsRequest(RestApiException ex);

        public async Task<IImmutableList<Channel>> ListChannelsAsync(
            string classification = default,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await ListChannelsInternalAsync(
                classification,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task<HttpOperationResponse<IImmutableList<Channel>>> ListChannelsInternalAsync(
            string classification = default,
            CancellationToken cancellationToken = default
        )
        {
            const string apiVersion = "2019-01-16";

            var _path = "/api/channels";

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(classification))
            {
                _query.Add("classification", Client.Serialize(classification));
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
                    HandleFailedListChannelsRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<IImmutableList<Channel>>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<IImmutableList<Channel>>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedCreateChannelRequest(RestApiException ex);

        public async Task<Channel> CreateChannelAsync(
            string classification,
            string name,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await CreateChannelInternalAsync(
                classification,
                name,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task<HttpOperationResponse<Channel>> CreateChannelInternalAsync(
            string classification,
            string name,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrEmpty(classification))
            {
                throw new ArgumentNullException(nameof(classification));
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/channels";

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(name))
            {
                _query.Add("name", Client.Serialize(name));
            }
            if (!string.IsNullOrEmpty(classification))
            {
                _query.Add("classification", Client.Serialize(classification));
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
                    HandleFailedCreateChannelRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<Channel>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<Channel>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedGetChannelRequest(RestApiException ex);

        public async Task<Channel> GetChannelAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetChannelInternalAsync(
                id,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task<HttpOperationResponse<Channel>> GetChannelInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default)
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/channels/{id}";
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
                    HandleFailedGetChannelRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<Channel>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<Channel>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedDeleteChannelRequest(RestApiException ex);

        public async Task<Channel> DeleteChannelAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await DeleteChannelInternalAsync(
                id,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task<HttpOperationResponse<Channel>> DeleteChannelInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default)
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/channels/{id}";
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
                    HandleFailedDeleteChannelRequest(ex);
                    HandleFailedRequest(ex);
                    Client.OnFailedRequest(ex);
                    throw ex;
                }
                _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<Channel>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<Channel>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedAddBuildToChannelRequest(RestApiException ex);

        public async Task AddBuildToChannelAsync(
            int buildId,
            int channelId,
            CancellationToken cancellationToken = default
        )
        {
            using (await AddBuildToChannelInternalAsync(
                buildId,
                channelId,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return;
            }
        }

        internal async Task<HttpOperationResponse> AddBuildToChannelInternalAsync(
            int buildId,
            int channelId,
            CancellationToken cancellationToken = default
        )
        {
            if (buildId == default)
            {
                throw new ArgumentNullException(nameof(buildId));
            }

            if (channelId == default)
            {
                throw new ArgumentNullException(nameof(channelId));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/channels/{channelId}/builds/{buildId}";
            _path = _path.Replace("{channelId}", Client.Serialize(channelId));
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
                    HandleFailedAddBuildToChannelRequest(ex);
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

        partial void HandleFailedAddPipelineToChannelRequest(RestApiException ex);

        public async Task AddPipelineToChannelAsync(
            int channelId,
            int pipelineId,
            CancellationToken cancellationToken = default
        )
        {
            using (await AddPipelineToChannelInternalAsync(
                channelId,
                pipelineId,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return;
            }
        }

        internal async Task<HttpOperationResponse> AddPipelineToChannelInternalAsync(
            int channelId,
            int pipelineId,
            CancellationToken cancellationToken = default
        )
        {
            if (channelId == default)
            {
                throw new ArgumentNullException(nameof(channelId));
            }

            if (pipelineId == default)
            {
                throw new ArgumentNullException(nameof(pipelineId));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/channels/{channelId}/pipelines/{pipelineId}";
            _path = _path.Replace("{channelId}", Client.Serialize(channelId));
            _path = _path.Replace("{pipelineId}", Client.Serialize(pipelineId));

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
                    HandleFailedAddPipelineToChannelRequest(ex);
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

        partial void HandleFailedDeletePipelineFromChannelRequest(RestApiException ex);

        public async Task DeletePipelineFromChannelAsync(
            int channelId,
            int pipelineId,
            CancellationToken cancellationToken = default
        )
        {
            using (await DeletePipelineFromChannelInternalAsync(
                channelId,
                pipelineId,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return;
            }
        }

        internal async Task<HttpOperationResponse> DeletePipelineFromChannelInternalAsync(
            int channelId,
            int pipelineId,
            CancellationToken cancellationToken = default
        )
        {
            if (channelId == default)
            {
                throw new ArgumentNullException(nameof(channelId));
            }

            if (pipelineId == default)
            {
                throw new ArgumentNullException(nameof(pipelineId));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/channels/{channelId}/pipelines/{pipelineId}";
            _path = _path.Replace("{channelId}", Client.Serialize(channelId));
            _path = _path.Replace("{pipelineId}", Client.Serialize(pipelineId));

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
                    HandleFailedDeletePipelineFromChannelRequest(ex);
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
