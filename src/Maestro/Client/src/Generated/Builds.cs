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
    public partial interface IBuilds
    {
        AsyncPageable<Models.Build> ListBuildsAsync(
            string azdoAccount = default,
            int? azdoBuildId = default,
            string azdoProject = default,
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task<Page<Models.Build>> ListBuildsPageAsync(
            string azdoAccount = default,
            int? azdoBuildId = default,
            string azdoProject = default,
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task<Models.Build> CreateAsync(
            Models.BuildData body,
            CancellationToken cancellationToken = default
        );

        Task<Models.Build> GetBuildAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<Models.BuildGraph> GetBuildGraphAsync(
            int id,
            CancellationToken cancellationToken = default
        );

        Task<Models.Build> GetLatestAsync(
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task<Models.Build> UpdateAsync(
            Models.BuildUpdate body,
            int buildId,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Builds : IServiceOperations<MaestroApi>, IBuilds
    {
        public Builds(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListBuildsRequest(RestApiException ex);

        public AsyncPageable<Models.Build> ListBuildsAsync(
            string azdoAccount = default,
            int? azdoBuildId = default,
            string azdoProject = default,
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            async IAsyncEnumerable<Page<Models.Build>> GetPages(string _continueToken, int? _pageSizeHint)
            {
                int? page = 1;
                int? perPage = _pageSizeHint;

                if (!string.IsNullOrEmpty(_continueToken))
                {
                    page = int.Parse(_continueToken);
                }

                while (true)
                {
                    Page<Models.Build> _page = null;

                    try {
                        _page = await ListBuildsPageAsync(
                            azdoAccount,
                            azdoBuildId,
                            azdoProject,
                            buildNumber,
                            channelId,
                            commit,
                            loadCollections,
                            notAfter,
                            notBefore,
                            page,
                            perPage,
                            repository,
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

        public async Task<Page<Models.Build>> ListBuildsPageAsync(
            string azdoAccount = default,
            int? azdoBuildId = default,
            string azdoProject = default,
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/builds",
                false);

            if (!string.IsNullOrEmpty(repository))
            {
                _url.AppendQuery("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(commit))
            {
                _url.AppendQuery("commit", Client.Serialize(commit));
            }
            if (!string.IsNullOrEmpty(buildNumber))
            {
                _url.AppendQuery("buildNumber", Client.Serialize(buildNumber));
            }
            if (azdoBuildId != default(int?))
            {
                _url.AppendQuery("azdoBuildId", Client.Serialize(azdoBuildId));
            }
            if (!string.IsNullOrEmpty(azdoAccount))
            {
                _url.AppendQuery("azdoAccount", Client.Serialize(azdoAccount));
            }
            if (!string.IsNullOrEmpty(azdoProject))
            {
                _url.AppendQuery("azdoProject", Client.Serialize(azdoProject));
            }
            if (channelId != default(int?))
            {
                _url.AppendQuery("channelId", Client.Serialize(channelId));
            }
            if (notBefore != default(DateTimeOffset?))
            {
                _url.AppendQuery("notBefore", Client.Serialize(notBefore));
            }
            if (notAfter != default(DateTimeOffset?))
            {
                _url.AppendQuery("notAfter", Client.Serialize(notAfter));
            }
            if (loadCollections != default(bool?))
            {
                _url.AppendQuery("loadCollections", Client.Serialize(loadCollections));
            }
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
                        await OnListBuildsFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnListBuildsFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<IImmutableList<Models.Build>>(_content);
                        return Page<Models.Build>.FromValues(_body, (page + 1).ToString(), _res);
                    }
                }
            }
        }

        internal async Task OnListBuildsFailed(Request req, Response res)
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
            HandleFailedListBuildsRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedCreateRequest(RestApiException ex);

        public async Task<Models.Build> CreateAsync(
            Models.BuildData body,
            CancellationToken cancellationToken = default
        )
        {

            if (body == default(Models.BuildData))
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
                "/api/builds",
                false);

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                if (body != default(Models.BuildData))
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
                        var _body = Client.Deserialize<Models.Build>(_content);
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

        partial void HandleFailedGetBuildRequest(RestApiException ex);

        public async Task<Models.Build> GetBuildAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {

            if (id == default(int))
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/builds/{id}".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
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
                        await OnGetBuildFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetBuildFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Build>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetBuildFailed(Request req, Response res)
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
            HandleFailedGetBuildRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetBuildGraphRequest(RestApiException ex);

        public async Task<Models.BuildGraph> GetBuildGraphAsync(
            int id,
            CancellationToken cancellationToken = default
        )
        {

            if (id == default(int))
            {
                throw new ArgumentNullException(nameof(id));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/builds/{id}/graph".Replace("{id}", Uri.EscapeDataString(Client.Serialize(id))),
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
                        await OnGetBuildGraphFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetBuildGraphFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.BuildGraph>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetBuildGraphFailed(Request req, Response res)
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
            HandleFailedGetBuildGraphRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetLatestRequest(RestApiException ex);

        public async Task<Models.Build> GetLatestAsync(
            string buildNumber = default,
            int? channelId = default,
            string commit = default,
            bool? loadCollections = default,
            DateTimeOffset? notAfter = default,
            DateTimeOffset? notBefore = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/builds/latest",
                false);

            if (!string.IsNullOrEmpty(repository))
            {
                _url.AppendQuery("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(commit))
            {
                _url.AppendQuery("commit", Client.Serialize(commit));
            }
            if (!string.IsNullOrEmpty(buildNumber))
            {
                _url.AppendQuery("buildNumber", Client.Serialize(buildNumber));
            }
            if (channelId != default(int?))
            {
                _url.AppendQuery("channelId", Client.Serialize(channelId));
            }
            if (notBefore != default(DateTimeOffset?))
            {
                _url.AppendQuery("notBefore", Client.Serialize(notBefore));
            }
            if (notAfter != default(DateTimeOffset?))
            {
                _url.AppendQuery("notAfter", Client.Serialize(notAfter));
            }
            if (loadCollections != default(bool?))
            {
                _url.AppendQuery("loadCollections", Client.Serialize(loadCollections));
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
                        await OnGetLatestFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetLatestFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Build>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetLatestFailed(Request req, Response res)
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
            HandleFailedGetLatestRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedUpdateRequest(RestApiException ex);

        public async Task<Models.Build> UpdateAsync(
            Models.BuildUpdate body,
            int buildId,
            CancellationToken cancellationToken = default
        )
        {

            if (body == default(Models.BuildUpdate))
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (buildId == default(int))
            {
                throw new ArgumentNullException(nameof(buildId));
            }

            const string apiVersion = "2020-02-20";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/builds/{buildId}".Replace("{buildId}", Uri.EscapeDataString(Client.Serialize(buildId))),
                false);

            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Patch;

                if (body != default(Models.BuildUpdate))
                {
                    _req.Content = RequestContent.Create(Encoding.UTF8.GetBytes(Client.Serialize(body)));
                    _req.Headers.Add("Content-Type", "application/json; charset=utf-8");
                }

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnUpdateFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnUpdateFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<Models.Build>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnUpdateFailed(Request req, Response res)
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
            HandleFailedUpdateRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
