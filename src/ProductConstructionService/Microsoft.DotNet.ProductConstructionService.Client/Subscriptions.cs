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
    /// Any manually applied changes need to live in partial classes outside of the "Generated" folder

    internal partial class Subscriptions : IServiceOperations<ProductConstructionServiceApi>, ISubscriptions
    {
        public async Task<Models.Subscription> TriggerSubscriptionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return await TriggerSubscriptionAsync(default, id, cancellationToken);
        }

        public async Task<Models.Subscription> TriggerSubscriptionAsync(Guid id, bool force, CancellationToken cancellationToken = default)
        {
            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscriptions/{id}/trigger".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
                false);

            if (force)
            {
                _url.AppendQuery("force", Client.Serialize(force));
            }
            _url.AppendQuery("api-version", Client.Serialize(apiVersion));

            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                using (var _res = await Client.Pipeline.SendRequestAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status == 202)
                    {
                        using (var _reader = new StreamReader(_res.ContentStream))
                        {
                            var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                            return Client.Deserialize<Models.Subscription>(_content);
                        }
                    }
                    else
                    {
                        throw new RequestFailedException(_res);
                    }
                }
            }
        }

        public async Task<Models.Subscription> TriggerSubscriptionAsync(Guid id, int buildId, bool force, CancellationToken cancellationToken = default)
        {
            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/subscriptions/{id}/trigger".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
                false);

            if (buildId != default)
            {
                _url.AppendQuery("bar-build-id", Client.Serialize(buildId));
            }
            if (force)
            {
                _url.AppendQuery("force", Client.Serialize(force));
            }
            _url.AppendQuery("api-version", Client.Serialize(apiVersion));

            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                using (var _res = await Client.Pipeline.SendRequestAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status == 202)
                    {
                        using (var _reader = new StreamReader(_res.ContentStream))
                        {
                            var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                            return Client.Deserialize<Models.Subscription>(_content);
                        }
                    }
                    else
                    {
                        throw new RequestFailedException(_res);
                    }
                }
            }
        }
    }

    public partial interface ISubscriptions
    {
        Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            bool force,
            CancellationToken cancellationToken = default
        );

        Task<Models.Subscription> TriggerSubscriptionAsync(
            Guid id,
            int buildId,
            bool force,
            CancellationToken cancellationToken = default
        );
    }
}
