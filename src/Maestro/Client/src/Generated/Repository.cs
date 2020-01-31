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
    public partial interface IRepository
    {
        Task<IImmutableList<Models.RepositoryBranch>> ListRepositoriesAsync(
            string branch = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task<IImmutableList<Models.MergePolicy>> GetMergePoliciesAsync(
            string branch,
            string repository,
            CancellationToken cancellationToken = default
        );

        Task SetMergePoliciesAsync(
            string branch,
            string repository,
            IImmutableList<Models.MergePolicy> body = default,
            CancellationToken cancellationToken = default
        );

        AsyncPageable<Models.RepositoryHistoryItem> GetHistoryAsync(
            string branch = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task<Page<Models.RepositoryHistoryItem>> GetHistoryPageAsync(
            string branch = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        );

        Task RetryActionAsyncAsync(
            string branch,
            string repository,
            long timestamp,
            CancellationToken cancellationToken = default
        );

    }

    internal partial class Repository : IServiceOperations<MaestroApi>, IRepository
    {
        public Repository(MaestroApi client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public MaestroApi Client { get; }

        partial void HandleFailedRequest(RestApiException ex);

        partial void HandleFailedListRepositoriesRequest(RestApiException ex);

        public async Task<IImmutableList<Models.RepositoryBranch>> ListRepositoriesAsync(
            string branch = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2019-01-16";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/repo-config/repositories",
                false);

            if (!string.IsNullOrEmpty(repository))
            {
                _url.AppendQuery("repository", Client.Serialize(repository));
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
                        await OnListRepositoriesFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnListRepositoriesFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<IImmutableList<Models.RepositoryBranch>>(_content);
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

        partial void HandleFailedGetMergePoliciesRequest(RestApiException ex);

        public async Task<IImmutableList<Models.MergePolicy>> GetMergePoliciesAsync(
            string branch,
            string repository,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(repository))
            {
                throw new ArgumentNullException(nameof(repository));
            }

            const string apiVersion = "2019-01-16";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/repo-config/merge-policy",
                false);

            if (!string.IsNullOrEmpty(repository))
            {
                _url.AppendQuery("repository", Client.Serialize(repository));
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
                        await OnGetMergePoliciesFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetMergePoliciesFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<IImmutableList<Models.MergePolicy>>(_content);
                        return _body;
                    }
                }
            }
        }

        internal async Task OnGetMergePoliciesFailed(Request req, Response res)
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
            HandleFailedGetMergePoliciesRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedSetMergePoliciesRequest(RestApiException ex);

        public async Task SetMergePoliciesAsync(
            string branch,
            string repository,
            IImmutableList<Models.MergePolicy> body = default,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(repository))
            {
                throw new ArgumentNullException(nameof(repository));
            }

            const string apiVersion = "2019-01-16";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/repo-config/merge-policy",
                false);

            if (!string.IsNullOrEmpty(repository))
            {
                _url.AppendQuery("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _url.AppendQuery("branch", Client.Serialize(branch));
            }
            _url.AppendQuery("api-version", Client.Serialize(apiVersion));


            using (var _req = Client.Pipeline.CreateRequest())
            {
                _req.Uri = _url;
                _req.Method = RequestMethod.Post;

                if (body != default(IImmutableList<Models.MergePolicy>))
                {
                    _req.Content = RequestContent.Create(Encoding.UTF8.GetBytes(Client.Serialize(body)));
                    _req.Headers.Add("Content-Type", "application/json; charset=utf-8");
                }

                using (var _res = await Client.SendAsync(_req, cancellationToken).ConfigureAwait(false))
                {
                    if (_res.Status < 200 || _res.Status >= 300)
                    {
                        await OnSetMergePoliciesFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnSetMergePoliciesFailed(Request req, Response res)
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
            HandleFailedSetMergePoliciesRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedGetHistoryRequest(RestApiException ex);

        public AsyncPageable<Models.RepositoryHistoryItem> GetHistoryAsync(
            string branch = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {
            async IAsyncEnumerable<Page<Models.RepositoryHistoryItem>> GetPages(string _continueToken, int? _pageSizeHint)
            {
                int? page = 1;
                int? perPage = _pageSizeHint;

                if (!string.IsNullOrEmpty(_continueToken))
                {
                    page = int.Parse(_continueToken);
                }

                while (true)
                {
                    Page<Models.RepositoryHistoryItem> _page = null;

                    try {
                        _page = await GetHistoryPageAsync(
                            branch,
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
                    catch (RestApiException) when (e.Response.Status == 404)
                    {
                        yield break;
                    }

                    yield return _page;
                    page++;
                }
            }
            return AsyncPageable.Create(GetPages);
        }

        public async Task<Page<Models.RepositoryHistoryItem>> GetHistoryPageAsync(
            string branch = default,
            int? page = default,
            int? perPage = default,
            string repository = default,
            CancellationToken cancellationToken = default
        )
        {

            const string apiVersion = "2019-01-16";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/repo-config/history",
                false);

            if (!string.IsNullOrEmpty(repository))
            {
                _url.AppendQuery("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _url.AppendQuery("branch", Client.Serialize(branch));
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
                        await OnGetHistoryFailed(_req, _res).ConfigureAwait(false);
                    }

                    if (_res.ContentStream == null)
                    {
                        await OnGetHistoryFailed(_req, _res).ConfigureAwait(false);
                    }

                    using (var _reader = new StreamReader(_res.ContentStream))
                    {
                        var _content = await _reader.ReadToEndAsync().ConfigureAwait(false);
                        var _body = Client.Deserialize<IImmutableList<Models.RepositoryHistoryItem>>(_content);
                        return Page<Models.RepositoryHistoryItem>.FromValues(_body, (page + 1).ToString(), _res);
                    }
                }
            }
        }

        internal async Task OnGetHistoryFailed(Request req, Response res)
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
            HandleFailedGetHistoryRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }

        partial void HandleFailedRetryActionAsyncRequest(RestApiException ex);

        public async Task RetryActionAsyncAsync(
            string branch,
            string repository,
            long timestamp,
            CancellationToken cancellationToken = default
        )
        {

            if (string.IsNullOrEmpty(branch))
            {
                throw new ArgumentNullException(nameof(branch));
            }

            if (string.IsNullOrEmpty(repository))
            {
                throw new ArgumentNullException(nameof(repository));
            }

            if (timestamp == default(long))
            {
                throw new ArgumentNullException(nameof(timestamp));
            }

            const string apiVersion = "2019-01-16";

            var _baseUri = Client.Options.BaseUri;
            var _url = new RequestUriBuilder();
            _url.Reset(_baseUri);
            _url.AppendPath(
                "/api/repo-config/retry/{timestamp}".Replace("{timestamp}", Uri.EscapeDataString(Client.Serialize(timestamp))),
                false);

            if (!string.IsNullOrEmpty(repository))
            {
                _url.AppendQuery("repository", Client.Serialize(repository));
            }
            if (!string.IsNullOrEmpty(branch))
            {
                _url.AppendQuery("branch", Client.Serialize(branch));
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
                        await OnRetryActionAsyncFailed(_req, _res).ConfigureAwait(false);
                    }


                    return;
                }
            }
        }

        internal async Task OnRetryActionAsyncFailed(Request req, Response res)
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
            HandleFailedRetryActionAsyncRequest(ex);
            HandleFailedRequest(ex);
            Client.OnFailedRequest(ex);
            throw ex;
        }
    }
}
