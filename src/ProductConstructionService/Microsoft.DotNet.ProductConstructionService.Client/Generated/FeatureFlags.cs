// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;



namespace Microsoft.DotNet.ProductConstructionService.Client
{
    public partial interface IFeatureFlags
    {
        Task<Models.FeatureFlagResponse> SetFeatureFlagAsync(
            Models.SetFeatureFlagRequest body = default,
            CancellationToken cancellationToken = default
        );

        Task<Models.FeatureFlagListResponse> GetAllFeatureFlagsAsync(
            CancellationToken cancellationToken = default
        );

        Task<Models.FeatureFlagListResponse> GetFeatureFlagsAsync(
            Guid subscriptionId,
            CancellationToken cancellationToken = default
        );

        Task<Models.FeatureFlagValue> GetFeatureFlagAsync(
            string flagName,
            Guid subscriptionId,
            CancellationToken cancellationToken = default
        );

        Task<bool> RemoveFeatureFlagAsync(
            string flagName,
            Guid subscriptionId,
            CancellationToken cancellationToken = default
        );

        Task<Models.AvailableFeatureFlagsResponse> GetAvailableFeatureFlagsAsync(
            CancellationToken cancellationToken = default
        );

        Task<Models.FeatureFlagListResponse> GetSubscriptionsWithFlagAsync(
            string flagName,
            CancellationToken cancellationToken = default
        );

        Task<Models.RemoveFlagFromAllResponse> RemoveFlagFromAllSubscriptionsAsync(
            string flagName,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class FeatureFlags : IServiceOperations<ProductConstructionServiceApi>, IFeatureFlags
    {
        public FeatureFlags(ProductConstructionServiceApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public ProductConstructionServiceApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedSetFeatureFlagRequest(RestApiException ex);

        public async Task<Models.FeatureFlagResponse> SetFeatureFlagAsync(
            Models.SetFeatureFlagRequest body = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/feature-flags",
                false);

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                if (body != default(Models.SetFeatureFlagRequest))
                {
                    _req.Content = RequestContent.Create(Encoding.UTF8.GetBytes(Client.Serialize(body)));
                    _req.Headers.Add("Content-Type", "application/json; charset=utf-8");
                }

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnSetFeatureFlagFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnSetFeatureFlagFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.FeatureFlagResponse>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnSetFeatureFlagFailed(Request req, Response res)
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
            HandleFailedSetFeatureFlagRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetAllFeatureFlagsRequest(RestApiException ex);

        public async Task<Models.FeatureFlagListResponse> GetAllFeatureFlagsAsync(
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/feature-flags",
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
                        await OnGetAllFeatureFlagsFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetAllFeatureFlagsFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.FeatureFlagListResponse>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetAllFeatureFlagsFailed(Request req, Response res)
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
            HandleFailedGetAllFeatureFlagsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetFeatureFlagsRequest(RestApiException ex);

        public async Task<Models.FeatureFlagListResponse> GetFeatureFlagsAsync(
            Guid subscriptionId,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/feature-flags/{subscriptionId}".Replace("{subscriptionId}", Uri.EscapeDataString(Client.Serialize(subscriptionId))),
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
                        await OnGetFeatureFlagsFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetFeatureFlagsFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.FeatureFlagListResponse>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetFeatureFlagsFailed(Request req, Response res)
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
            HandleFailedGetFeatureFlagsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetFeatureFlagRequest(RestApiException ex);

        public async Task<Models.FeatureFlagValue> GetFeatureFlagAsync(
            string flagName,
            Guid subscriptionId,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(flagName))
            {
                throw new ArgumentNullException(nameof(flagName));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/feature-flags/{subscriptionId}/{flagName}".Replace("{subscriptionId}", Uri.EscapeDataString(Client.Serialize(subscriptionId))).Replace("{flagName}", Uri.EscapeDataString(Client.Serialize(flagName))),
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
                        await OnGetFeatureFlagFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetFeatureFlagFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.FeatureFlagValue>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetFeatureFlagFailed(Request req, Response res)
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
            HandleFailedGetFeatureFlagRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedRemoveFeatureFlagRequest(RestApiException ex);

        public async Task<bool> RemoveFeatureFlagAsync(
            string flagName,
            Guid subscriptionId,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(flagName))
            {
                throw new ArgumentNullException(nameof(flagName));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/feature-flags/{subscriptionId}/{flagName}".Replace("{subscriptionId}", Uri.EscapeDataString(Client.Serialize(subscriptionId))).Replace("{flagName}", Uri.EscapeDataString(Client.Serialize(flagName))),
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
                        await OnRemoveFeatureFlagFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnRemoveFeatureFlagFailed(_req, _res).ConfigureAwait(false);
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

        internal async Task OnRemoveFeatureFlagFailed(Request req, Response res)
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
            HandleFailedRemoveFeatureFlagRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetAvailableFeatureFlagsRequest(RestApiException ex);

        public async Task<Models.AvailableFeatureFlagsResponse> GetAvailableFeatureFlagsAsync(
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/feature-flags/available",
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
                        await OnGetAvailableFeatureFlagsFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetAvailableFeatureFlagsFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.AvailableFeatureFlagsResponse>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetAvailableFeatureFlagsFailed(Request req, Response res)
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
            HandleFailedGetAvailableFeatureFlagsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetSubscriptionsWithFlagRequest(RestApiException ex);

        public async Task<Models.FeatureFlagListResponse> GetSubscriptionsWithFlagAsync(
            string flagName,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(flagName))
            {
                throw new ArgumentNullException(nameof(flagName));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/feature-flags/by-flag/{flagName}".Replace("{flagName}", Uri.EscapeDataString(Client.Serialize(flagName))),
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
                        await OnGetSubscriptionsWithFlagFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetSubscriptionsWithFlagFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.FeatureFlagListResponse>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetSubscriptionsWithFlagFailed(Request req, Response res)
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
            HandleFailedGetSubscriptionsWithFlagRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedRemoveFlagFromAllSubscriptionsRequest(RestApiException ex);

        public async Task<Models.RemoveFlagFromAllResponse> RemoveFlagFromAllSubscriptionsAsync(
            string flagName,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(flagName))
            {
                throw new ArgumentNullException(nameof(flagName));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/feature-flags/by-flag/{flagName}".Replace("{flagName}", Uri.EscapeDataString(Client.Serialize(flagName))),
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
                        await OnRemoveFlagFromAllSubscriptionsFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnRemoveFlagFromAllSubscriptionsFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.RemoveFlagFromAllResponse>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnRemoveFlagFromAllSubscriptionsFailed(Request req, Response res)
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
            HandleFailedRemoveFlagFromAllSubscriptionsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
