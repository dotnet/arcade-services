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
    public partial interface IConfiguration
    {
        Task<bool> RefreshConfigurationAsync(
            string branch,
            string repoUri,
            CancellationToken cancellationToken = default
        );

        Task<bool> ClearConfigurationAsync(
            string branch,
            string repoUri,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Configuration : IServiceOperations<ProductConstructionServiceApi>, IConfiguration
    {
        public Configuration(ProductConstructionServiceApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ProductConstructionServiceApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedRefreshConfigurationRequest(RestApiException ex);

        public async Task<bool> RefreshConfigurationAsync(
            string branch,
            string repoUri,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(repoUri))
            {
                throw new ArgumentNullException(nameof(repoUri));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/configuration",
                false);

            if (!string.IsNullOrEmpty(repoUri))
            {
                _url.AppendQuery("repoUri", Client.Serialize(repoUri));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _url.AppendQuery("branch", Client.Serialize(branch));
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
                        await OnRefreshConfigurationFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnRefreshConfigurationFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<bool>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnRefreshConfigurationFailed(Request req, Response res)
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
            HandleFailedRefreshConfigurationRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedClearConfigurationRequest(RestApiException ex);

        public async Task<bool> ClearConfigurationAsync(
            string branch,
            string repoUri,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(repoUri))
            {
                throw new ArgumentNullException(nameof(repoUri));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/configuration",
                false);

            if (!string.IsNullOrEmpty(repoUri))
            {
                _url.AppendQuery("repoUri", Client.Serialize(repoUri));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _url.AppendQuery("branch", Client.Serialize(branch));
            }
            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Delete;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnClearConfigurationFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnClearConfigurationFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<bool>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnClearConfigurationFailed(Request req, Response res)
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
            HandleFailedClearConfigurationRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
