using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Microsoft.DotNet.Maestro.Client.Models;

namespace Microsoft.DotNet.Maestro.Client
{
    public partial interface IBuildTime
    {
        Task<Models.BuildTime> GetBuildTimesAsync(
            int days,
            int id,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class BuildTime : IServiceOperations<MaestroApi>, IBuildTime
    {
        public BuildTime(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedGetBuildTimesRequest(RestApiException ex);

        public async Task<Models.BuildTime> GetBuildTimesAsync(
            int days,
            int id,
            CancellationToken cancellationToken = default
        )
        {
            if (days == default(int))
            {
                throw new ArgumentNullException(nameof(days));
            }

            if (id == default(int))
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2019-01-16";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/buildtime/{id}".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
                false);

            if (days != default(int))
            {
                _url.AppendQuery("days", Client.Serialize(days));
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
                        return new Models.BuildTime(id, 0, 0, 0);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetBuildTimesFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.BuildTime>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetBuildTimesFailed(Request req, Response res)
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
            HandleFailedGetBuildTimesRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
