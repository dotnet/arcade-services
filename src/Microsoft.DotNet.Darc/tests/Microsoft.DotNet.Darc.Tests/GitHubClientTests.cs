// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib;
using Microsoft.Extensions.FileSystemGlobbing.Internal.PatternContexts;
using Octokit;
using Xunit;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.TeamFoundation.WorkItemTracking.Process.WebApi.Models;
using Capture = Moq.Capture;
using GitHubClient = Microsoft.DotNet.DarcLib.GitHubClient;
using GitHubCommit = Microsoft.DotNet.DarcLib.GitHubCommit;
using PullRequest = Octokit.PullRequest;

namespace Microsoft.DotNet.Darc.Tests
{
    public class SimpleCacheEntry : ICacheEntry
    {
        private object _key;
        private object _value;
        private long? _size;

        public SimpleCacheEntry(object key)
        {
            _key = key;
        }

        public object Key => _key;

        public object Value { get => _value; set => _value = value; }
        public DateTimeOffset? AbsoluteExpiration { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public TimeSpan? AbsoluteExpirationRelativeToNow { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public TimeSpan? SlidingExpiration { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public IList<IChangeToken> ExpirationTokens => throw new NotImplementedException();

        public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks => throw new NotImplementedException();

        public CacheItemPriority Priority { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public long? Size { get => _size; set => _size = value; }

        public void Dispose() { }
    }

    public class SimpleCache : IMemoryCache
    {
        public int CacheHits { get; set; }
        public int CacheMisses { get; set; }
        public ConcurrentDictionary<object, ICacheEntry> cache = new ConcurrentDictionary<object, ICacheEntry>();

        public ICacheEntry CreateEntry(object key)
        {
            var newEntry = new SimpleCacheEntry(key);
            return cache.AddOrUpdate(key, new SimpleCacheEntry(key), (existingKey, existingValue) => newEntry);
        }

        public void Dispose()
        {
            
        }

        public void Remove(object key)
        {
            cache.Remove(key, out ICacheEntry unused);
        }

        public bool TryGetValue(object key, out object value)
        {
            if (cache.TryGetValue(key, out ICacheEntry existingEntry))
            {
                // GitHubClient should be setting the size of the 
                // entries (they should be non-zero).
                Assert.True(existingEntry.Size > 0);
                CacheHits++;
                value = existingEntry.Value;
                return true;
            }
            else
            {
                CacheMisses++;
                value = null;
                return false;
            }
        }
    }

    public class GitHubClientTests
    {
        protected readonly Mock<IGitHubClient> GithubClient = new Mock<IGitHubClient>();


        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        private async Task TreeItemCacheTest(bool enableCache)
        {
            SimpleCache cache = enableCache ? new SimpleCache() : null;
            Mock<DarcLib.GitHubClient> client = new Mock<DarcLib.GitHubClient>(null, null, NullLogger.Instance, null, cache);

            List<(string, string, Octokit.TreeItem)> treeItemsToGet = new List<(string, string, Octokit.TreeItem)>
            {
                ("a", "b", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "1", "https://url")),
                ("a", "b", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "2", "https://url")),
                ("a", "b", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "3", "https://url")),
                ("a", "b", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "4", "https://url")),
                ("dotnet", "corefx", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "11", "https://url")),
                ("dotnet", "corefx", new Octokit.TreeItem("path", "mode", Octokit.TreeType.Blob, 10, "12", "https://url")),
            };

            // Mock up the github client
            var octoKitClientMock = new Mock<Octokit.IGitHubClient>();
            var octoKitGitMock = new Mock<Octokit.IGitDatabaseClient>();
            var octoKitBlobClientMock = new Mock<Octokit.IBlobsClient>();
            Octokit.Blob blob = new Octokit.Blob("foo", "content", Octokit.EncodingType.Utf8, "somesha", 10);

            foreach (var treeItem in treeItemsToGet)
            {
                octoKitBlobClientMock.Setup(m => m.Get(treeItem.Item1, treeItem.Item2, treeItem.Item3.Sha)).ReturnsAsync(blob);
            }

            octoKitGitMock.Setup(m => m.Blob).Returns(octoKitBlobClientMock.Object);
            octoKitClientMock.Setup(m => m.Git).Returns(octoKitGitMock.Object);
            client.Setup(m => m.Client).Returns(octoKitClientMock.Object);

            // Request all but the last tree item in the list, then request the full set, then again.
            // For the cache scenario, we should have no cache hits on first pass, n-1 on the second, and N on the last
            // For the no-cache scenario, we simply not crash.

            for (int i = 0; i < treeItemsToGet.Count - 1; i++)
            {
                await client.Object.GetGitTreeItem("path", treeItemsToGet[i].Item3, treeItemsToGet[i].Item1, treeItemsToGet[i].Item2);
            }

            int expectedCacheHits = 0;
            int expectedCacheMisses = treeItemsToGet.Count - 1;
            if (enableCache)
            {
                Assert.Equal(expectedCacheMisses, cache.CacheMisses);
                Assert.Equal(expectedCacheHits, cache.CacheHits);
            }

            // Request full set
            for (int i = 0; i < treeItemsToGet.Count; i++)
            {
                await client.Object.GetGitTreeItem("path", treeItemsToGet[i].Item3, treeItemsToGet[i].Item1, treeItemsToGet[i].Item2);
            }

            if (enableCache)
            {
                expectedCacheMisses++;
                expectedCacheHits += (treeItemsToGet.Count - 1);
                Assert.Equal(treeItemsToGet.Count, cache.CacheMisses);
                Assert.Equal(treeItemsToGet.Count - 1, cache.CacheHits);
            }

            // Request full set
            for (int i = 0; i < treeItemsToGet.Count; i++)
            {
                await client.Object.GetGitTreeItem("path", treeItemsToGet[i].Item3, treeItemsToGet[i].Item1, treeItemsToGet[i].Item2);
            }

            if (enableCache)
            {
                expectedCacheHits += treeItemsToGet.Count;
                Assert.Equal(expectedCacheMisses, cache.CacheMisses);
                Assert.Equal(expectedCacheHits, cache.CacheHits);
            }

            // Request an item with the same SHA but different path
            var renamedTreeItem = treeItemsToGet[0];
            var renamedTreeItemBlob = renamedTreeItem.Item3;
            renamedTreeItem.Item3 = new Octokit.TreeItem("anotherPath",
                renamedTreeItemBlob.Mode,
                Octokit.TreeType.Blob,
                renamedTreeItemBlob.Size,
                renamedTreeItemBlob.Sha,
                renamedTreeItemBlob.Url);

            await client.Object.GetGitTreeItem("anotherPath", renamedTreeItem.Item3, renamedTreeItem.Item1, renamedTreeItem.Item2);

            if (enableCache)
            {
                // First time the new item should not be in the cache
                expectedCacheMisses++;
                Assert.Equal(expectedCacheMisses, cache.CacheMisses);
                Assert.Equal(expectedCacheHits, cache.CacheHits);
                // Get it again, this time it should be in the cache
                expectedCacheHits++;
                await client.Object.GetGitTreeItem("anotherPath", treeItemsToGet[0].Item3, treeItemsToGet[0].Item1, treeItemsToGet[0].Item2);
                Assert.Equal(expectedCacheHits, cache.CacheHits);
            }
        }

        [Theory]
        [InlineData("fakeURl", true, "fakeCommit", false )]
        private async Task MergeDependencyTest(string pullRequestUrl, bool deleteSourceBranch , string commitToMerge, bool squashMerge)
        {
            //Mock<GitHubClient> darc = new Mock<GitHubClient>(null, null, NullLogger.Instance, null);
            string owner = "testOwner";
            string repo = "testRepo";
            int repoId = 1;
            //darc.Setup(x => x.ParsePullRequestUri(It.IsAny<string>())).Returns((owner, repo, repoId));
            MergePullRequestParameters mergePullRequest = new MergePullRequestParameters
            {
                DeleteSourceBranch = deleteSourceBranch,
                CommitToMerge = commitToMerge,
                SquashMerge = squashMerge
            };

            var parents = new GitReference[1];
            var firstCommit = new Octokit.Commit(
                "testNode",
                "testUrl",
                "testLabe;",
                "testRef",
                "testSha",
                null,
                null,
                @"[branchName] Update dependencies from maestro-auth-test/maestro-test1

- Bar: from  to 2.2.0
- Foo: from  to 1.2.0",
                null,
                null,
                null,
                parents,
                1,
                null
            );
            var dotNetBot = new Committer(
                "dotnet-maestro[bot]",
                "test@email.com",
                new DateTime(2020, 01, 01)
                );
            var pullrequestCommit = new PullRequestCommit(
                "nodeUrl",
                null,
                "commentUrl",
                firstCommit,
                dotNetBot,
                null,
                parents,
                null,
                null);

            var test = new Committer(
                "user",
                "test@email.com",
                new DateTime(2020, 01, 01)
            );

            var secondCommit = new PullRequestCommit(
                "nodeUrl",
                null,
                "commentUrl",
                firstCommit,
                test,
                null,
                parents,
                null,
                null);

            var commits = new List<PullRequestCommit>();
            commits.Add(pullrequestCommit);

            GithubClient.Setup(x => x.PullRequest.Get(owner, repo, 1)).ReturnsAsync(GetPullReq);
            GithubClient.Setup(x => x.PullRequest.Commits(owner, repo, 1)).ReturnsAsync(commits);

            string dependencyUpdate = "Dependency Update";
            string coherencyUpdate = "Coherency Update";

            //darc.Setup(x => x.ParsePullRequestBody(It.IsAny<Regex>(), It.IsAny<string>())).Returns(dependencyUpdate);
            //darc.Setup(x => x.ParsePullRequestBody(It.IsAny<Regex>(), It.IsAny<string>())).Returns(coherencyUpdate);

            List<MergePullRequest> merge = new List<MergePullRequest>();
            PullRequestMerge prMerge = new PullRequestMerge();
/*            merge.CommitMessage = $@"{dependencyUpdate} 
{coherencyUpdate}";
            merge.CommitTitle = "Title for PR";
            merge.Sha = "TestSha";*/

            //GithubClient.Setup(x => x.PullRequest.Merge(owner, repo, 1, Capture.In(merge))).ReturnsAsync(prMerge);
            GithubClient.Setup(x => x.PullRequest.Merge(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), Capture.In(merge)));
            GithubClient.Verify(x=>x.PullRequest.Merge(It.IsAny<string>(), It.IsAny<string>(),It.IsAny<int>(), It.IsAny<MergePullRequest>()), Times.Once);

            

        }

        public PullRequest GetPullReq()
        {
            var pr = new Octokit.PullRequest
            (
                1,
                "testModId",
                "testUrl",
                "testHtml",
                "testUrl",
                "patchUrl",
                "issueUrl",
                "statusesUrl",
                1,
                ItemState.Open,
                "Title for PR",
                "Body",
                new DateTime(2020, 01, 01),
                new DateTime(2020, 01, 01),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                false,
                null,
                null,
                "testSha",
                0,
                2,
                0,
                0,
                0,
                null,
                false,
                false,
                null,
                null
            );
            return pr;
        }
        /*      [Theory]

              [InlineData((@"- \*\*(?<updates>[\w+\.\-]+)\*\*: from (?<oldVersion>[\w\.\-]*) to (?<newVersion>[\w\.\-]+)"), @"
      ## Coherency Updates

      The following updates ensure that dependencies with a *CoherentParentDependency*
      attribute were produced in a build used as input to the parent dependency's build.
      See [Dependency Description Format](https://github.com/dotnet/arcade/blob/master/Documentation/DependencyDescriptionFormat.md#dependency-description-overview)
       -  **Microsoft.NETCore.App.Internal**: from 3.1.4-servicing.20214.5 to 3.1.4-servicing.20221.3
       -  **Microsoft.NETCore.App.Runtime.win-x64**: from 3.1.4 to 3.1.4

      ## From https://dev.azure.com/dnceng/internal/_git/maestro-test1
      - **Subscription**: b2a4bbef-8dc3-4cb3-5a13-08d818a46851
      - **Build**: 165387918
      - **Date Produced**: 6/25/2020 1:09 AM
      - **Commit**: 1819542737
      - **Branch**: master
      - **Updates**:
        - **Foo.sdasd**: from 123 to 1.2.0
        - **Bar.sdasdasd.jgjgkj**: from 2323 to 2.2.0
        - **Microsoft.NETCore.App.Internal**: from 3.1.4-servicing.20214.5 to 3.1.4-servicing.20221.3
        - **Microsoft.NETCore.App.Runtime.win-x64**: from 3.1.4 to 3.1.4


      [marker]: <> (End:eaa13548-9011-4b1b-00f7-08d81963b896)
      ")]
              public void ParsePullRequestTest(string patternDetails, string body)
              {
                  Regex pattern = new Regex(patternDetails);
                  Mock<Regex> patterns = new Mock<Regex>();

                  //var matches = new MatchCollection();
      // patterns.Setup(x => x.Matches(body)).Returns(matches);




              }
        */
    }
}
