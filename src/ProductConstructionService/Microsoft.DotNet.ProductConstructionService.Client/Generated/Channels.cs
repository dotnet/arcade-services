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
    public partial interface IChannels
    {
        Task<List<Models.Channel>> ListChannelsAsync(
            string classification = default,
            CancellationToken cancellationToken = default
        );

        Task<Models.Channel> CreateChannelAsync(
            string classification,
            string name,
            CancellationToken cancellationToken = default
        );

        Task<List<string>> ListRepositoriesAsync(
            int id,
            int? withBuildsInDays = default,
            CancellationToken cancellationToken = default
        );

        Task<Models.Channel> GetChannelAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<Models.Channel> DeleteChannelAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<Models.Channel> UpdateChannelAsync(
            int id,
            string classification = default,
            string name = default,
            CancellationToken cancellationToken = default
        );

        Task AddBuildToChannelAsync(
            int buildId,
            int channelId,
            CancellationToken cancellationToken = default
        );

        Task RemoveBuildFromChannelAsync(
            int buildId,
            int channelId,
            CancellationToken cancellationToken = default
        );

        Task<Models.FlowGraph> GetFlowGraphAsync(
            int days,
            int channelId,
            bool includeArcade,
            bool includeBuildTimes,
            bool includeDisabledSubscriptions,
            List<string> includedFrequencies = default,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Channels : IServiceOperations<ProductConstructionServiceApi>, IChannels
    {
        public Channels(ProductConstructionServiceApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ProductConstructionServiceApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListChannelsRequest(RestApiException ex);

        public async Task<List<Models.Channel>> ListChannelsAsync(
            string classification = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/channels",
                false);

            if (!string.IsNullOrEmpty(classification))
            {
                _url.AppendQuery("classification", Client.Serialize(classification));
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
                        await OnListChannelsFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnListChannelsFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<List<Models.Channel>>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnListChannelsFailed(Request req, Response res)
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
            HandleFailedListChannelsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedCreateChannelRequest(RestApiException ex);

        public async Task<Models.Channel> CreateChannelAsync(
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

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/channels",
                false);

            if (!string.IsNullOrEmpty(name))
            {
                _url.AppendQuery("name", Client.Serialize(name));
            }
            if (!string.IsNullOrEmpty(classification))
            {
                _url.AppendQuery("classification", Client.Serialize(classification));
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
                        await OnCreateChannelFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnCreateChannelFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Channel>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnCreateChannelFailed(Request req, Response res)
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
            HandleFailedCreateChannelRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedListRepositoriesRequest(RestApiException ex);

        public async Task<List<string>> ListRepositoriesAsync(
            int id,
            int? withBuildsInDays = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/channels/{id}/repositories".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
                false);

            if (withBuildsInDays != default)
            {
                _url.AppendQuery("withBuildsInDays", Client.Serialize(withBuildsInDays));
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
                        await OnListRepositoriesFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnListRepositoriesFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<List<string>>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnListRepositoriesFailed(Request req, Response res)
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
            HandleFailedListRepositoriesRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetChannelRequest(RestApiException ex);

        public async Task<Models.Channel> GetChannelAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/channels/{id}".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
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
                        await OnGetChannelFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetChannelFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Channel>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetChannelFailed(Request req, Response res)
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
            HandleFailedGetChannelRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedDeleteChannelRequest(RestApiException ex);

        public async Task<Models.Channel> DeleteChannelAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/channels/{id}".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
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
                        await OnDeleteChannelFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnDeleteChannelFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Channel>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnDeleteChannelFailed(Request req, Response res)
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
            HandleFailedDeleteChannelRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedUpdateChannelRequest(RestApiException ex);

        public async Task<Models.Channel> UpdateChannelAsync(
            int id,
            string classification = default,
            string name = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/channels/{id}".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
                false);

            if (!string.IsNullOrEmpty(name))
            {
                _url.AppendQuery("name", Client.Serialize(name));
            }
            if (!string.IsNullOrEmpty(classification))
            {
                _url.AppendQuery("classification", Client.Serialize(classification));
            }
            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Patch;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnUpdateChannelFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnUpdateChannelFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Channel>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnUpdateChannelFailed(Request req, Response res)
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
            HandleFailedUpdateChannelRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedAddBuildToChannelRequest(RestApiException ex);

        public async Task AddBuildToChannelAsync(
            int buildId,
            int channelId,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/channels/{channelId}/builds/{buildId}".Replace("{channelId}", Uri.EscapeDataString(Client.Serialize(channelId))).Replace("{buildId}", Uri.EscapeDataString(Client.Serialize(buildId))),
                false);

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnAddBuildToChannelFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnAddBuildToChannelFailed(Request req, Response res)
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
            HandleFailedAddBuildToChannelRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedRemoveBuildFromChannelRequest(RestApiException ex);

        public async Task RemoveBuildFromChannelAsync(
            int buildId,
            int channelId,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/channels/{channelId}/builds/{buildId}".Replace("{channelId}", Uri.EscapeDataString(Client.Serialize(channelId))).Replace("{buildId}", Uri.EscapeDataString(Client.Serialize(buildId))),
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
                        await OnRemoveBuildFromChannelFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnRemoveBuildFromChannelFailed(Request req, Response res)
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
            HandleFailedRemoveBuildFromChannelRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetFlowGraphRequest(RestApiException ex);

        public async Task<Models.FlowGraph> GetFlowGraphAsync(
            int days,
            int channelId,
            bool includeArcade,
            bool includeBuildTimes,
            bool includeDisabledSubscriptions,
            List<string> includedFrequencies = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/channels/{channelId}/graph".Replace("{channelId}", Uri.EscapeDataString(Client.Serialize(channelId))),
                false);

            if (includeDisabledSubscriptions != default)
            {
                _url.AppendQuery("includeDisabledSubscriptions", Client.Serialize(includeDisabledSubscriptions));
            }
            if (includedFrequencies != default(List<string>))
            {
                foreach (var _item in includedFrequencies)
                {
                    _url.AppendQuery("includedFrequencies", Client.Serialize(_item));
                }
            }
            if (includeBuildTimes != default)
            {
                _url.AppendQuery("includeBuildTimes", Client.Serialize(includeBuildTimes));
            }
            if (days != default)
            {
                _url.AppendQuery("days", Client.Serialize(days));
            }
            if (includeArcade != default)
            {
                _url.AppendQuery("includeArcade", Client.Serialize(includeArcade));
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
                        await OnGetFlowGraphFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetFlowGraphFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.FlowGraph>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetFlowGraphFailed(Request req, Response res)
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
            HandleFailedGetFlowGraphRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
