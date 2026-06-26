// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;



namespace Microsoft.DotNet.ProductConstructionService.Client
{
    public partial interface ICodeflow
    {
        Task<IList<Models.CodeflowStatus>> GetCodeflowStatusesAsync(
            string branch,
            string repositoryUrl,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Codeflow : IServiceOperations<ProductConstructionServiceApi>, ICodeflow
    {
        public Codeflow(ProductConstructionServiceApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ProductConstructionServiceApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedGetCodeflowStatusesRequest(RestApiException ex);

        public async Task<IList<Models.CodeflowStatus>> GetCodeflowStatusesAsync(
            string branch,
            string repositoryUrl,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(repositoryUrl))
            {
                throw new ArgumentNullException(nameof(repositoryUrl));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/codeflows",
                false);

            if (!string.IsNullOrEmpty(repositoryUrl))
            {
                _url.AppendQuery("repositoryUrl", Client.Serialize(repositoryUrl));
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
                        await OnGetCodeflowStatusesFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetCodeflowStatusesFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<IList<Models.CodeflowStatus>>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetCodeflowStatusesFailed(Request req, Response res)
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
            HandleFailedGetCodeflowStatusesRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
