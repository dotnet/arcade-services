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
    public partial interface IPipelines
    {
        Task<IImmutableList<ReleasePipeline>> ListAsync(
            string organization = default,
            int? pipelineIdentifier = default,
            string project = default,
            CancellationToken cancellationToken = default
        );

        Task<ReleasePipeline> CreatePipelineAsync(
            string organization,
            int pipelineIdentifier,
            string project,
            CancellationToken cancellationToken = default
        );

        Task<ReleasePipeline> GetPipelineAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<ReleasePipeline> DeletePipelineAsync(
            int id,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Pipelines : IServiceOperations<MaestroApi>, IPipelines
    {
        public Pipelines(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListRequest(RestApiException ex);

        public async Task<IImmutableList<ReleasePipeline>> ListAsync(
            string organization = default,
            int? pipelineIdentifier = default,
            string project = default,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await ListInternalAsync(
                organization,
                pipelineIdentifier,
                project,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnListFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedListRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<IImmutableList<ReleasePipeline>>> ListInternalAsync(
            string organization = default,
            int? pipelineIdentifier = default,
            string project = default,
            CancellationToken cancellationToken = default
        )
        {
            const string apiVersion = "2019-01-16";

            var _path = "/api/pipelines";

            var _query = new QueryBuilder();
            if (pipelineIdentifier != default)
            {
                _query.Add("pipelineIdentifier", Client.Serialize(pipelineIdentifier));
            }
            if (!string.IsNullOrEmpty(organization))
            {
                _query.Add("organization", Client.Serialize(organization));
            }
            if (!string.IsNullOrEmpty(project))
            {
                _query.Add("project", Client.Serialize(project));
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
                    await OnListFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<IImmutableList<ReleasePipeline>>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<IImmutableList<ReleasePipeline>>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedCreatePipelineRequest(RestApiException ex);

        public async Task<ReleasePipeline> CreatePipelineAsync(
            string organization,
            int pipelineIdentifier,
            string project,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await CreatePipelineInternalAsync(
                organization,
                pipelineIdentifier,
                project,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnCreatePipelineFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedCreatePipelineRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<ReleasePipeline>> CreatePipelineInternalAsync(
            string organization,
            int pipelineIdentifier,
            string project,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrEmpty(organization))
            {
                throw new ArgumentNullException(nameof(organization));
            }

            if (pipelineIdentifier == default)
            {
                throw new ArgumentNullException(nameof(pipelineIdentifier));
            }

            if (string.IsNullOrEmpty(project))
            {
                throw new ArgumentNullException(nameof(project));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/pipelines";

            var _query = new QueryBuilder();
            if (pipelineIdentifier != default)
            {
                _query.Add("pipelineIdentifier", Client.Serialize(pipelineIdentifier));
            }
            if (!string.IsNullOrEmpty(organization))
            {
                _query.Add("organization", Client.Serialize(organization));
            }
            if (!string.IsNullOrEmpty(project))
            {
                _query.Add("project", Client.Serialize(project));
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
                if (!_res.IsSuccessStatusCode)
                {
                    await OnCreatePipelineFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<ReleasePipeline>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<ReleasePipeline>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedGetPipelineRequest(RestApiException ex);

        public async Task<ReleasePipeline> GetPipelineAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetPipelineInternalAsync(
                id,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnGetPipelineFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedGetPipelineRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<ReleasePipeline>> GetPipelineInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default)
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/pipelines/{id}";
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
                    await OnGetPipelineFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<ReleasePipeline>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<ReleasePipeline>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedDeletePipelineRequest(RestApiException ex);

        public async Task<ReleasePipeline> DeletePipelineAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await DeletePipelineInternalAsync(
                id,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnDeletePipelineFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedDeletePipelineRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<ReleasePipeline>> DeletePipelineInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default)
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/pipelines/{id}";
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
                if (!_res.IsSuccessStatusCode)
                {
                    await OnDeletePipelineFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<ReleasePipeline>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<ReleasePipeline>(_responseContent),
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
