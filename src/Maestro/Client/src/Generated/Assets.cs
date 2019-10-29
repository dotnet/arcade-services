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
    public partial interface IAssets
    {
        Task<PagedResponse<Asset>> ListAssetsAsync(
            int? buildId = default,
            bool? loadLocations = default,
            string name = default,
            bool? nonShipping = default,
            int? page = default,
            int? perPage = default,
            string version = default,
            CancellationToken cancellationToken = default
        );

        Task<string> GetDarcVersionAsync(
            CancellationToken cancellationToken = default
        );

        Task<Asset> GetAssetAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<AssetLocation> AddAssetLocationToAssetAsync(
            int assetId,
            AddAssetLocationToAssetAssetLocationType assetLocationType,
            string location,
            CancellationToken cancellationToken = default
        );

        Task RemoveAssetLocationFromAssetAsync(
            int assetId,
            int assetLocationId,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Assets : IServiceOperations<MaestroApi>, IAssets
    {
        public Assets(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListAssetsRequest(RestApiException ex);

        public async Task<PagedResponse<Asset>> ListAssetsAsync(
            int? buildId = default,
            bool? loadLocations = default,
            string name = default,
            bool? nonShipping = default,
            int? page = default,
            int? perPage = default,
            string version = default,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await ListAssetsInternalAsync(
                buildId,
                loadLocations,
                name,
                nonShipping,
                page,
                perPage,
                version,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return new PagedResponse<Asset>(Client, OnListAssetsFailed, _res);
            }
        }

        internal async Task OnListAssetsFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedListAssetsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<IImmutableList<Asset>>> ListAssetsInternalAsync(
            int? buildId = default,
            bool? loadLocations = default,
            string name = default,
            bool? nonShipping = default,
            int? page = default,
            int? perPage = default,
            string version = default,
            CancellationToken cancellationToken = default
        )
        {
            const string apiVersion = "2019-01-16";

            var _path = "/api/assets";

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(name))
            {
                _query.Add("name", Client.Serialize(name));
            }
            if (!string.IsNullOrEmpty(version))
            {
                _query.Add("version", Client.Serialize(version));
            }
            if (buildId != default(int?))
            {
                _query.Add("buildId", Client.Serialize(buildId));
            }
            if (nonShipping != default(bool?))
            {
                _query.Add("nonShipping", Client.Serialize(nonShipping));
            }
            if (loadLocations != default(bool?))
            {
                _query.Add("loadLocations", Client.Serialize(loadLocations));
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
                    await OnListAssetsFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<IImmutableList<Asset>>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<IImmutableList<Asset>>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedGetDarcVersionRequest(RestApiException ex);

        public async Task<string> GetDarcVersionAsync(
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetDarcVersionInternalAsync(
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnGetDarcVersionFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedGetDarcVersionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<string>> GetDarcVersionInternalAsync(
            CancellationToken cancellationToken = default
        )
        {
            const string apiVersion = "2019-01-16";

            var _path = "/api/assets/darc-version";

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
                    await OnGetDarcVersionFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<string>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<string>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedGetAssetRequest(RestApiException ex);

        public async Task<Asset> GetAssetAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await GetAssetInternalAsync(
                id,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnGetAssetFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedGetAssetRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<Asset>> GetAssetInternalAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (id == default(int))
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/assets/{id}";
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
                    await OnGetAssetFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<Asset>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<Asset>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedAddAssetLocationToAssetRequest(RestApiException ex);

        public async Task<AssetLocation> AddAssetLocationToAssetAsync(
            int assetId,
            AddAssetLocationToAssetAssetLocationType assetLocationType,
            string location,
            CancellationToken cancellationToken = default
        )
        {
            using (var _res = await AddAssetLocationToAssetInternalAsync(
                assetId,
                assetLocationType,
                location,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return _res.Body;
            }
        }

        internal async Task OnAddAssetLocationToAssetFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedAddAssetLocationToAssetRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse<AssetLocation>> AddAssetLocationToAssetInternalAsync(
            int assetId,
            AddAssetLocationToAssetAssetLocationType assetLocationType,
            string location,
            CancellationToken cancellationToken = default
        )
        {
            if (assetId == default(int))
            {
                throw new ArgumentNullException(nameof(assetId));
            }

            if (assetLocationType == default(AddAssetLocationToAssetAssetLocationType))
            {
                throw new ArgumentNullException(nameof(assetLocationType));
            }

            if (string.IsNullOrEmpty(location))
            {
                throw new ArgumentNullException(nameof(location));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/assets/{assetId}/locations";
            _path = _path.Replace("{assetId}", Client.Serialize(assetId));

            var _query = new QueryBuilder();
            if (!string.IsNullOrEmpty(location))
            {
                _query.Add("location", Client.Serialize(location));
            }
            if (assetLocationType != default(AddAssetLocationToAssetAssetLocationType))
            {
                _query.Add("assetLocationType", Client.Serialize(assetLocationType));
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
                    await OnAddAssetLocationToAssetFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new HttpOperationResponse<AssetLocation>
                {
                    Request = _req,
                    Response = _res,
                    Body = Client.Deserialize<AssetLocation>(_responseContent),
                };
            }
            catch (Exception)
            {
                _req?.Dispose();
                _res?.Dispose();
                throw;
            }
        }

        partial void HandleFailedRemoveAssetLocationFromAssetRequest(RestApiException ex);

        public async Task RemoveAssetLocationFromAssetAsync(
            int assetId,
            int assetLocationId,
            CancellationToken cancellationToken = default
        )
        {
            using (await RemoveAssetLocationFromAssetInternalAsync(
                assetId,
                assetLocationId,
                cancellationToken
            ).ConfigureAwait(false))
            {
                return;
            }
        }

        internal async Task OnRemoveAssetLocationFromAssetFailed(HttpRequestMessage req, HttpResponseMessage res)
        {
            var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            var ex = new RestApiException<ApiError>(
                new HttpRequestMessageWrapper(req, null),
                new HttpResponseMessageWrapper(res, content),
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedRemoveAssetLocationFromAssetRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        internal async Task<HttpOperationResponse> RemoveAssetLocationFromAssetInternalAsync(
            int assetId,
            int assetLocationId,
            CancellationToken cancellationToken = default
        )
        {
            if (assetId == default(int))
            {
                throw new ArgumentNullException(nameof(assetId));
            }

            if (assetLocationId == default(int))
            {
                throw new ArgumentNullException(nameof(assetLocationId));
            }

            const string apiVersion = "2019-01-16";

            var _path = "/api/assets/{assetId}/locations/{assetLocationId}";
            _path = _path.Replace("{assetId}", Client.Serialize(assetId));
            _path = _path.Replace("{assetLocationId}", Client.Serialize(assetLocationId));

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
                    await OnRemoveAssetLocationFromAssetFailed(_req, _res);
                }
                string _responseContent = await _res.Content.ReadAsStringAsync().ConfigureAwait(false);
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
