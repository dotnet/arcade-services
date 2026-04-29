// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;



namespace Microsoft.DotNet.ProductConstructionService.Client
{
    public partial interface IMinDarcVersion
    {
        Task<string> GetMinDarcVersionAsync(
            CancellationToken cancellationToken = default
        );

        Task SetMinDarcVersionAsync(
            string minimumVersion,
            CancellationToken cancellationToken = default
        );

        Task ClearMinDarcVersionAsync(
            CancellationToken cancellationToken = default
        );

    }

    internal partial class MinDarcVersion : IServiceOperations<ProductConstructionServiceApi>, IMinDarcVersion
    {
        public MinDarcVersion(ProductConstructionServiceApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ProductConstructionServiceApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedGetMinDarcVersionRequest(RestApiException ex);

        public async Task<string> GetMinDarcVersionAsync(
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/min-darc-version",
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
                        await OnGetMinDarcVersionFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetMinDarcVersionFailed(_req, _res).ConfigureAwait(false);
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

        internal async Task OnGetMinDarcVersionFailed(Request req, Response res)
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
            HandleFailedGetMinDarcVersionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedSetMinDarcVersionRequest(RestApiException ex);

        public async Task SetMinDarcVersionAsync(
            string minimumVersion,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(minimumVersion))
            {
                throw new ArgumentNullException(nameof(minimumVersion));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/min-darc-version",
                false);

            if (!string.IsNullOrEmpty(minimumVersion))
            {
                _url.AppendQuery("minimumVersion", Client.Serialize(minimumVersion));
            }
            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Put;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnSetMinDarcVersionFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnSetMinDarcVersionFailed(Request req, Response res)
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
            HandleFailedSetMinDarcVersionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedClearMinDarcVersionRequest(RestApiException ex);

        public async Task ClearMinDarcVersionAsync(
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/min-darc-version",
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
                        await OnClearMinDarcVersionFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnClearMinDarcVersionFailed(Request req, Response res)
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
            HandleFailedClearMinDarcVersionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
