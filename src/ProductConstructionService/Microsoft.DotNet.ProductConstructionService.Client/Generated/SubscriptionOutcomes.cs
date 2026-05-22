using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;



namespace Microsoft.DotNet.ProductConstructionService.Client
{
    public partial interface ISubscriptionOutcomes
    {
        Task<IImmutableList<Models.SubscriptionOutcome>> ListSubscriptionOutcomesAsync(
            int limit,
            int? buildId = default,
            DateTimeOffset? date = default,
            string operationId = default,
            string subscriptionId = default,
            string subscriptionOutcomeType = default,
            CancellationToken cancellationToken = default
        );

        Task<Models.SubscriptionOutcome> GetSubscriptionOutcomeAsync(
            string operationId,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class SubscriptionOutcomes : IServiceOperations<ProductConstructionServiceApi>, ISubscriptionOutcomes
    {
        public SubscriptionOutcomes(ProductConstructionServiceApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ProductConstructionServiceApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListSubscriptionOutcomesRequest(RestApiException ex);

        public async Task<IImmutableList<Models.SubscriptionOutcome>> ListSubscriptionOutcomesAsync(
            int limit,
            int? buildId = default,
            DateTimeOffset? date = default,
            string operationId = default,
            string subscriptionId = default,
            string subscriptionOutcomeType = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscription-outcomes",
                false);

            if (!string.IsNullOrEmpty(subscriptionId))
            {
                _url.AppendQuery("subscriptionId", Client.Serialize(subscriptionId));
            }
            if (buildId != default(int?))
            {
                _url.AppendQuery("buildId", Client.Serialize(buildId));
            }
            if (date != default(DateTimeOffset?))
            {
                _url.AppendQuery("date", Client.Serialize(date));
            }
            if (!string.IsNullOrEmpty(subscriptionOutcomeType))
            {
                _url.AppendQuery("subscriptionOutcomeType", Client.Serialize(subscriptionOutcomeType));
            }
            if (!string.IsNullOrEmpty(operationId))
            {
                _url.AppendQuery("operationId", Client.Serialize(operationId));
            }
            if (limit != default(int))
            {
                _url.AppendQuery("limit", Client.Serialize(limit));
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
                        await OnListSubscriptionOutcomesFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnListSubscriptionOutcomesFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<IImmutableList<Models.SubscriptionOutcome>>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnListSubscriptionOutcomesFailed(Request req, Response res)
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
            HandleFailedListSubscriptionOutcomesRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetSubscriptionOutcomeRequest(RestApiException ex);

        public async Task<Models.SubscriptionOutcome> GetSubscriptionOutcomeAsync(
            string operationId,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(operationId))
            {
                throw new ArgumentNullException(nameof(operationId));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscription-outcomes/{operationId}".Replace("{operationId}", Uri.EscapeDataString(Client.Serialize(operationId))),
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
                        await OnGetSubscriptionOutcomeFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetSubscriptionOutcomeFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.SubscriptionOutcome>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetSubscriptionOutcomeFailed(Request req, Response res)
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
            HandleFailedGetSubscriptionOutcomeRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
