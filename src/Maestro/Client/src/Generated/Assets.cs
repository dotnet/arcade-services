using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;



namespace Microsoft.DotNet.Maestro.Client
{
    public partial interface IAssets
    {
        Task BulkAddLocationsAsync(
            IImmutableList<Models.AssetAndLocation> body,
            CancellationToken cancellationToken = default
        );

        AsyncPageable<Models.Asset> ListAssetsAsync(
            int? buildId = default,
            bool? loadLocations = default,
            string name = default,
            bool? nonShipping = default,
            string version = default,
            CancellationToken cancellationToken = default
        );

        Task<Page<Models.Asset>> ListAssetsPageAsync(
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

        Task<Models.Asset> GetAssetAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<Models.AssetLocation> AddAssetLocationToAssetAsync(
            int assetId,
            Models.LocationType assetLocationType,
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

        partial void HandleFailedBulkAddLocationsRequest(RestApiException ex);

        public async Task BulkAddLocationsAsync(
            IImmutableList<Models.AssetAndLocation> body,
            CancellationToken cancellationToken = default
        )
        {

            if (body == default(IImmutableList<Models.AssetAndLocation>))
            {
                throw new ArgumentNullException(nameof(body));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/assets/bulk-add-locations",
                false);

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                if (body != default(IImmutableList<Models.AssetAndLocation>))
                {
                    _req.Content = RequestContent.Create(Encoding.UTF8.GetBytes(Client.Serialize(body)));
                    _req.Headers.Add("Content-Type", "application/json; charset=utf-8");
                }

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnBulkAddLocationsFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnBulkAddLocationsFailed(Request req, Response res)
        {
            string content = null;
            if (res.ContentStream != null)
            {
                using (var reader = new StreamReader(res.ContentStream))
                {
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            var ex = new RestApiException<Models.ApiError>(
                req,
                res,
                content,
                Client.Deserialize<Models.ApiError>(content)
                );
            HandleFailedBulkAddLocationsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedListAssetsRequest(RestApiException ex);

        public AsyncPageable<Models.Asset> ListAssetsAsync(
            int? buildId = default,
            bool? loadLocations = default,
            string name = default,
            bool? nonShipping = default,
            string version = default,
            CancellationToken cancellationToken = default
        )
        {
            async IAsyncEnumerable<Page<Models.Asset>> GetPages(string _continueToken, int? _pageSizeHint)
            {
                int? page = 1;
                int? perPage = _pageSizeHint;

                if (!string.IsNullOrEmpty(_continueToken))
                {
                    page = int.Parse(_continueToken);
                }

                while (true)
                {
                    Page<Models.Asset> _page = null;

                    try {
                        _page = await ListAssetsPageAsync(
                            buildId,
                            loadLocations,
                            name,
                            nonShipping,
                            page,
                            perPage,
                            version,
                            cancellationToken
                        ).ConfigureAwait(false);
                        if (_page.Values.Count < 1)
                        {
                            yield break;
                        }                   
                    }
                    catch (RestApiException e) when (e.Response.Status == 404)
                    {
                        yield break;
                    }

                    yield return _page;
                    page++;
                }
            }
            return AsyncPageable.Create(GetPages);
        }

        public async Task<Page<Models.Asset>> ListAssetsPageAsync(
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

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/assets",
                false);

            if (!string.IsNullOrEmpty(name))
            {
                _url.AppendQuery("name", Client.Serialize(name));
            }
            if (!string.IsNullOrEmpty(version))
            {
                _url.AppendQuery("version", Client.Serialize(version));
            }
            if (buildId != default(int?))
            {
                _url.AppendQuery("buildId", Client.Serialize(buildId));
            }
            if (nonShipping != default(bool?))
            {
                _url.AppendQuery("nonShipping", Client.Serialize(nonShipping));
            }
            if (loadLocations != default(bool?))
            {
                _url.AppendQuery("loadLocations", Client.Serialize(loadLocations));
            }
            if (page != default(int?))
            {
                _url.AppendQuery("page", Client.Serialize(page));
            }
            if (perPage != default(int?))
            {
                _url.AppendQuery("perPage", Client.Serialize(perPage));
            }
            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Get;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnListAssetsFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnListAssetsFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<IImmutableList<Models.Asset>>(_content);
                        return Page<Models.Asset>.FromValues(_body, (page + 1).ToString(), _res);
                    }
                }
            }
        }

        internal async Task OnListAssetsFailed(Request req, Response res)
        {
            string content = null;
            if (res.ContentStream != null)
            {
                using (var reader = new StreamReader(res.ContentStream))
                {
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            var ex = new RestApiException<Models.ApiError>(
                req,
                res,
                content,
                Client.Deserialize<Models.ApiError>(content)
                );
            HandleFailedListAssetsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetDarcVersionRequest(RestApiException ex);

        public async Task<string> GetDarcVersionAsync(
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/assets/darc-version",
                false);

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Get;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnGetDarcVersionFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetDarcVersionFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<string>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetDarcVersionFailed(Request req, Response res)
        {
            string content = null;
            if (res.ContentStream != null)
            {
                using (var reader = new StreamReader(res.ContentStream))
                {
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            var ex = new RestApiException<Models.ApiError>(
                req,
                res,
                content,
                Client.Deserialize<Models.ApiError>(content)
                );
            HandleFailedGetDarcVersionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetAssetRequest(RestApiException ex);

        public async Task<Models.Asset> GetAssetAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/assets/{id}".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
                false);

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Get;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnGetAssetFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetAssetFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Asset>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetAssetFailed(Request req, Response res)
        {
            string content = null;
            if (res.ContentStream != null)
            {
                using (var reader = new StreamReader(res.ContentStream))
                {
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            var ex = new RestApiException<Models.ApiError>(
                req,
                res,
                content,
                Client.Deserialize<Models.ApiError>(content)
                );
            HandleFailedGetAssetRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedAddAssetLocationToAssetRequest(RestApiException ex);

        public async Task<Models.AssetLocation> AddAssetLocationToAssetAsync(
            int assetId,
            Models.LocationType assetLocationType,
            string location,
            CancellationToken cancellationToken = default
        )
        {

            if (assetLocationType == default(Models.LocationType))
            {
                throw new ArgumentNullException(nameof(assetLocationType));
            }

            if (string.IsNullOrEmpty(location))
            {
                throw new ArgumentNullException(nameof(location));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/assets/{assetId}/locations".Replace("{assetId}", Uri.EscapeDataString(Client.Serialize(assetId))),
                false);

            if (!string.IsNullOrEmpty(location))
            {
                _url.AppendQuery("location", Client.Serialize(location));
            }
            if (assetLocationType != default(Models.LocationType))
            {
                _url.AppendQuery("assetLocationType", Client.Serialize(assetLocationType));
            }
            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnAddAssetLocationToAssetFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnAddAssetLocationToAssetFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.AssetLocation>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnAddAssetLocationToAssetFailed(Request req, Response res)
        {
            string content = null;
            if (res.ContentStream != null)
            {
                using (var reader = new StreamReader(res.ContentStream))
                {
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            var ex = new RestApiException<Models.ApiError>(
                req,
                res,
                content,
                Client.Deserialize<Models.ApiError>(content)
                );
            HandleFailedAddAssetLocationToAssetRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedRemoveAssetLocationFromAssetRequest(RestApiException ex);

        public async Task RemoveAssetLocationFromAssetAsync(
            int assetId,
            int assetLocationId,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/assets/{assetId}/locations/{assetLocationId}".Replace("{assetId}", Uri.EscapeDataString(Client.Serialize(assetId))).Replace("{assetLocationId}", Uri.EscapeDataString(Client.Serialize(assetLocationId))),
                false);

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Delete;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnRemoveAssetLocationFromAssetFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnRemoveAssetLocationFromAssetFailed(Request req, Response res)
        {
            string content = null;
            if (res.ContentStream != null)
            {
                using (var reader = new StreamReader(res.ContentStream))
                {
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            var ex = new RestApiException<Models.ApiError>(
                req,
                res,
                content,
                Client.Deserialize<Models.ApiError>(content)
                );
            HandleFailedRemoveAssetLocationFromAssetRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
