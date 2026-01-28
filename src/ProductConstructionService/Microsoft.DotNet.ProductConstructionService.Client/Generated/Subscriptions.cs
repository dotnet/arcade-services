// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;



namespace Microsoft.DotNet.ProductConstructionService.Client
{
    public partial interface ISubscriptions
    {
        Task<List<Models.Subscription>> ListSubscriptionsAsync(
            bool? enabled = default,
            int? channelId = default,
            string sourceDirectory = default,
            bool? sourceEnabled = default,
            string sourceRepository = default,
            string targetDirectory = default,
            string targetRepository = default,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> GetSubscriptionAsync(
            Guid id,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> TriggerSubscriptionAsync(
            int barBuildId,
            bool force,
            Guid id,
            CancellationToken cancellationToken = default
        );

        Task TriggerDailyUpdateAsync(
            CancellationToken cancellationToken = default
        );

        AsyncPageable<Models.SubscriptionHistoryItem> GetSubscriptionHistoryAsync(
            Guid id,
            CancellationToken cancellationToken = default
        );

        Task<Page<Models.SubscriptionHistoryItem>> GetSubscriptionHistoryPageAsync(
            Guid id,
            int? page = default,
            int? perPage = default,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Subscriptions : IServiceOperations<ProductConstructionServiceApi>, ISubscriptions
    {
        public Subscriptions(ProductConstructionServiceApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ProductConstructionServiceApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListSubscriptionsRequest(RestApiException ex);

        public async Task<List<Models.Subscription>> ListSubscriptionsAsync(
            bool? enabled = default,
            int? channelId = default,
            string sourceDirectory = default,
            bool? sourceEnabled = default,
            string sourceRepository = default,
            string targetDirectory = default,
            string targetRepository = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscriptions",
                false);

            if (!string.IsNullOrEmpty(sourceRepository))
            {
                _url.AppendQuery("sourceRepository", Client.Serialize(sourceRepository));
            }
            if (!string.IsNullOrEmpty(targetRepository))
            {
                _url.AppendQuery("targetRepository", Client.Serialize(targetRepository));
            }
            if (channelId != default)
            {
                _url.AppendQuery("channelId", Client.Serialize(channelId));
            }
            if (enabled != default)
            {
                _url.AppendQuery("enabled", Client.Serialize(enabled));
            }
            if (sourceEnabled != default)
            {
                _url.AppendQuery("sourceEnabled", Client.Serialize(sourceEnabled));
            }
            if (!string.IsNullOrEmpty(sourceDirectory))
            {
                _url.AppendQuery("sourceDirectory", Client.Serialize(sourceDirectory));
            }
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                _url.AppendQuery("targetDirectory", Client.Serialize(targetDirectory));
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
                        await OnListSubscriptionsFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnListSubscriptionsFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<List<Models.Subscription>>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnListSubscriptionsFailed(Request req, Response res)
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
            HandleFailedListSubscriptionsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetSubscriptionRequest(RestApiException ex);

        public async Task<Models.Subscription> GetSubscriptionAsync(
            Guid id,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscriptions/{id}".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
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
                        await OnGetSubscriptionFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetSubscriptionFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Subscription>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetSubscriptionFailed(Request req, Response res)
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
            HandleFailedGetSubscriptionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedTriggerSubscriptionRequest(RestApiException ex);

        public async Task<Models.Subscription> TriggerSubscriptionAsync(
            int barBuildId,
            bool force,
            Guid id,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscriptions/{id}/trigger".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
                false);

            if (barBuildId != default)
            {
                _url.AppendQuery("bar-build-id", Client.Serialize(barBuildId));
            }
            if (force != default)
            {
                _url.AppendQuery("force", Client.Serialize(force));
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
                        await OnTriggerSubscriptionFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnTriggerSubscriptionFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Subscription>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnTriggerSubscriptionFailed(Request req, Response res)
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
            HandleFailedTriggerSubscriptionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedTriggerDailyUpdateRequest(RestApiException ex);

        public async Task TriggerDailyUpdateAsync(
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscriptions/triggerDaily",
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
                        await OnTriggerDailyUpdateFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnTriggerDailyUpdateFailed(Request req, Response res)
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
            HandleFailedTriggerDailyUpdateRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetSubscriptionHistoryRequest(RestApiException ex);

        public AsyncPageable<Models.SubscriptionHistoryItem> GetSubscriptionHistoryAsync(
            Guid id,
            CancellationToken cancellationToken = default
        )
        {
            async IAsyncEnumerable<Page<Models.SubscriptionHistoryItem>> GetPages(string _continueToken, int? _pageSizeHint)
            {
                int? page = 1;
                int? perPage = _pageSizeHint;

                if (!string.IsNullOrEmpty(_continueToken))
                {
                    page = int.Parse(_continueToken);
                }

                while (true)
                {
                    Page<Models.SubscriptionHistoryItem> _page = null;

                    try {
                        _page = await GetSubscriptionHistoryPageAsync(
                            id,
                            page,
                            perPage,
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

        public async Task<Page<Models.SubscriptionHistoryItem>> GetSubscriptionHistoryPageAsync(
            Guid id,
            int? page = default,
            int? perPage = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscriptions/{id}/history".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
                false);

            if (page != default)
            {
                _url.AppendQuery("page", Client.Serialize(page));
            }
            if (perPage != default)
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
                        await OnGetSubscriptionHistoryFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetSubscriptionHistoryFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<List<Models.SubscriptionHistoryItem>>(_content);
                        return Page<Models.SubscriptionHistoryItem>.FromValues(_body, (page + 1).ToString(), _res);
                    }
                }
            }
        }

        internal async Task OnGetSubscriptionHistoryFailed(Request req, Response res)
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
            HandleFailedGetSubscriptionHistoryRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
