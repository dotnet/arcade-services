// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;



namespace ProductConstructionService.Client
{
    public partial interface IAzDo
    {
        Task<Models.AzDoBuild> GetBuildStatusAsync(
            string account,
            string branch,
            int count,
            int definitionId,
            string project,
            string status,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class AzDo : IServiceOperations<ProductConstructionServiceApi>, IAzDo
    {
        public AzDo(ProductConstructionServiceApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ProductConstructionServiceApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedGetBuildStatusRequest(RestApiException ex);

        public async Task<Models.AzDoBuild> GetBuildStatusAsync(
            string account,
            string branch,
            int count,
            int definitionId,
            string project,
            string status,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(account))
            {
                throw new ArgumentNullException(nameof(account));
            }

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(project))
            {
                throw new ArgumentNullException(nameof(project));
            }

            if (string.IsNullOrEmpty(status))
            {
                throw new ArgumentNullException(nameof(status));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/azdo/build/status/{account}/{project}/{definitionId}/{branch}".Replace("{account}", Uri.EscapeDataString(Client.Serialize(account))).Replace("{project}", Uri.EscapeDataString(Client.Serialize(project))).Replace("{definitionId}", Uri.EscapeDataString(Client.Serialize(definitionId))).Replace("{branch}", Uri.EscapeDataString(Client.Serialize(branch))),
                false);

            if (count != default(int))
            {
                _url.AppendQuery("count", Client.Serialize(count));
            }
            if (!string.IsNullOrEmpty(status))
            {
                _url.AppendQuery("status", Client.Serialize(status));
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
                        await OnGetBuildStatusFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetBuildStatusFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.AzDoBuild>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetBuildStatusFailed(Request req, Response res)
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
            HandleFailedGetBuildStatusRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
