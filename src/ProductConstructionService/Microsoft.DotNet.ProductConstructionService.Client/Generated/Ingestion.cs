// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Microsoft.DotNet.ProductConstructionService.Client.Models;


namespace Microsoft.DotNet.ProductConstructionService.Client
{
    public partial interface IIngestion
    {
        Task<ConfigurationUpdates> IngestNamespaceAsync(
            string namespaceName,
            ClientYamlConfiguration body = default,
            CancellationToken cancellationToken = default
        );

        Task<bool> DeleteNamespaceAsync(
            string namespaceName,
            bool saveChanges,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Ingestion : IServiceOperations<ProductConstructionServiceApi>, IIngestion
    {
        public Ingestion(ProductConstructionServiceApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ProductConstructionServiceApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedIngestNamespaceRequest(RestApiException ex);

        public async Task<ConfigurationUpdates> IngestNamespaceAsync(
            string namespaceName,
            ClientYamlConfiguration body = default,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(namespaceName))
            {
                throw new ArgumentNullException(nameof(namespaceName));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/ingestion",
                false);

            if (!string.IsNullOrEmpty(namespaceName))
            {
                _url.AppendQuery("namespaceName", Client.Serialize(namespaceName));
            }
            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                if (body != default(ClientYamlConfiguration))
                {
                    _req.Content = RequestContent.Create(Encoding.UTF8.GetBytes(Client.Serialize(body)));
                    _req.Headers.Add("Content-Type", "application/json; charset=utf-8");
                }

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnIngestNamespaceFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnIngestNamespaceFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<ConfigurationUpdates>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnIngestNamespaceFailed(Request req, Response res)
        {
            string content = null;
            if (res.ContentStream != null)
            {
                using (var reader = new StreamReader(res.ContentStream))
                {
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            var ex = new RestApiException<ApiError>(
                req,
                res,
                content,
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedIngestNamespaceRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedDeleteNamespaceRequest(RestApiException ex);

        public async Task<bool> DeleteNamespaceAsync(
            string namespaceName,
            bool saveChanges,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(namespaceName))
            {
                throw new ArgumentNullException(nameof(namespaceName));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/ingestion",
                false);

            if (!string.IsNullOrEmpty(namespaceName))
            {
                _url.AppendQuery("namespaceName", Client.Serialize(namespaceName));
            }
            if (saveChanges != default(bool))
            {
                _url.AppendQuery("saveChanges", Client.Serialize(saveChanges));
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
                        await OnDeleteNamespaceFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnDeleteNamespaceFailed(_req, _res).ConfigureAwait(false);
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

        internal async Task OnDeleteNamespaceFailed(Request req, Response res)
        {
            string content = null;
            if (res.ContentStream != null)
            {
                using (var reader = new StreamReader(res.ContentStream))
                {
                    content = await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            var ex = new RestApiException<ApiError>(
                req,
                res,
                content,
                Client.Deserialize<ApiError>(content)
                );
            HandleFailedDeleteNamespaceRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
