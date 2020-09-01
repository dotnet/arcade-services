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
    public partial interface ISubscriptions
    {
        Task<IImmutableList<Models.Subscription>> ListSubscriptionsAsync(
            int? channelId = default,
            bool? enabled = default,
            string sourceRepository = default,
            string targetRepository = default,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> CreateAsync(
            Models.SubscriptionData body,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> GetSubscriptionAsync(
            Guid id,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> UpdateSubscriptionAsync(
            Guid id,
            Models.SubscriptionUpdate body = default,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> DeleteSubscriptionAsync(
            Guid id,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            int barBuildId,
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

        Task RetrySubscriptionActionAsync(
            Guid id,
            long timestamp,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Subscriptions : IServiceOperations<MaestroApi>, ISubscriptions
    {
        public Subscriptions(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListSubscriptionsRequest(RestApiException ex);

        public async Task<IImmutableList<Models.Subscription>> ListSubscriptionsAsync(
            int? channelId = default,
            bool? enabled = default,
            string sourceRepository = default,
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
            if (channelId != default(int?))
            {
                _url.AppendQuery("channelId", Client.Serialize(channelId));
            }
            if (enabled != default(bool?))
            {
                _url.AppendQuery("enabled", Client.Serialize(enabled));
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
                        var _body = Client.Deserialize<IImmutableList<Models.Subscription>>(_content);
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

        partial void HandleFailedCreateRequest(RestApiException ex);

        public async Task<Models.Subscription> CreateAsync(
            Models.SubscriptionData body,
            CancellationToken cancellationToken = default
        )
        {

            if (body == default(Models.SubscriptionData))
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (!body.IsValid)
            {
                throw new ArgumentException("The parameter is not valid", nameof(body));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscriptions",
                false);

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                if (body != default(Models.SubscriptionData))
                {
                    _req.Content = RequestContent.Create(Encoding.UTF8.GetBytes(Client.Serialize(body)));
                    _req.Headers.Add("Content-Type", "application/json; charset=utf-8");
                }

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnCreateFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnCreateFailed(_req, _res).ConfigureAwait(false);
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

        internal async Task OnCreateFailed(Request req, Response res)
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
            HandleFailedCreateRequest(ex);
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

        partial void HandleFailedUpdateSubscriptionRequest(RestApiException ex);

        public async Task<Models.Subscription> UpdateSubscriptionAsync(
            Guid id,
            Models.SubscriptionUpdate body = default,
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
                _req.Method = RequestMethod.Patch;

                if (body != default(Models.SubscriptionUpdate))
                {
                    _req.Content = RequestContent.Create(Encoding.UTF8.GetBytes(Client.Serialize(body)));
                    _req.Headers.Add("Content-Type", "application/json; charset=utf-8");
                }

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnUpdateSubscriptionFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnUpdateSubscriptionFailed(_req, _res).ConfigureAwait(false);
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

        internal async Task OnUpdateSubscriptionFailed(Request req, Response res)
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
            HandleFailedUpdateSubscriptionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedDeleteSubscriptionRequest(RestApiException ex);

        public async Task<Models.Subscription> DeleteSubscriptionAsync(
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
                _req.Method = RequestMethod.Delete;

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnDeleteSubscriptionFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnDeleteSubscriptionFailed(_req, _res).ConfigureAwait(false);
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

        internal async Task OnDeleteSubscriptionFailed(Request req, Response res)
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
            HandleFailedDeleteSubscriptionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedTriggerSubscriptionRequest(RestApiException ex);

        public async Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            CancellationToken cancellationToken = default
        )
        {
            return await TriggerSubscriptionAsync(id, 0, cancellationToken);
        }

        public async Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            int barBuildId,
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

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));

            // If the user specifies a particular build id to trigger subscriptions for, we'll provide that as a query parameter.
            if (barBuildId != 0)
            {
                _url.AppendQuery("bar-build-id", Client.Serialize(barBuildId));
            }

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
                        await OnGetSubscriptionHistoryFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetSubscriptionHistoryFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<IImmutableList<Models.SubscriptionHistoryItem>>(_content);
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

        partial void HandleFailedRetrySubscriptionActionRequest(RestApiException ex);

        public async Task RetrySubscriptionActionAsync(
            Guid id,
            long timestamp,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscriptions/{id}/retry/{timestamp}".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))).Replace("{timestamp}", Uri.EscapeDataString(Client.Serialize(timestamp))),
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
                        await OnRetrySubscriptionActionFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnRetrySubscriptionActionFailed(Request req, Response res)
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
            HandleFailedRetrySubscriptionActionRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
