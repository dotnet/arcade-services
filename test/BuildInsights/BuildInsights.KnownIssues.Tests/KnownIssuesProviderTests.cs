using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Kusto.Ingest;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using BuildInsights.KnownIssues.Models;
using BuildInsights.KnownIssues;
using Moq;
using NUnit.Framework;

namespace BuildInsights.KnownIssues.Tests
{
    [TestFixture]
    public class KnownIssuesProviderTests
    {
        private sealed class TestData : IDisposable, IAsyncDisposable
        {
            private readonly ServiceProvider _provider;
            public KnownIssuesProvider KnownIssuesProvider { get; }
            private TestData(ServiceProvider provider, KnownIssuesProvider knownIssuesProvider)
            {
                _provider = provider;
                KnownIssuesProvider = knownIssuesProvider;
            }
            public void Dispose()
            {
                _provider.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                return _provider.DisposeAsync();
            }

            public class Builder
            {
                private readonly Mock<IKustoIngestClientFactory> _kustoClientFactory = new Mock<IKustoIngestClientFactory>();
                private readonly Mock<IKustoClientProvider> _kustoClientProviderMock = new Mock<IKustoClientProvider>();
                private readonly Mock<IKustoIngestClient> _kustoIngestClientMock = new Mock<IKustoIngestClient>();
                private readonly Mock<IKnownIssuesHistoryService> _historyProviderMock = new Mock<IKnownIssuesHistoryService>();

                public Builder()
                {
                }

                private Builder(Mock<IKustoIngestClient> kustoIngestClientMock)
                {
                    _kustoIngestClientMock = kustoIngestClientMock;
                }

                public Builder With(Mock<IKustoIngestClient> kustoIngestClientMock = null)
                {
                    return new Builder(kustoIngestClientMock ?? _kustoIngestClientMock);
                }

                public Builder WithReader(IDataReader reader)
                {
                    _kustoClientProviderMock
                        .Setup(m => m.ExecuteKustoQueryAsync(It.IsAny<KustoQuery>()))
                        .ReturnsAsync(reader);
                    return this;
                }
                
                public TestData Build()
                {
                    ServiceCollection collection = new ServiceCollection();
                    collection.Configure<KustoOptions>(
                        o =>
                        {
                            o.Database = "TestDB";
                            o.KustoClusterUri = "QueryClusterUri";
                            o.KustoIngestionUri = "IngestConnectionString";
                            o.ManagedIdentityId = "ManagedIdentityId";  
                        }
                    );
                    collection.AddSingleton<ISystemClock, TestClock>();
                    collection.AddSingleton<IKnownIssuesService, KnownIssuesProvider>();

                    _kustoClientFactory.Setup(m => m.GetClient()).Returns(_kustoIngestClientMock.Object);

                    ServiceProvider services = collection
                        .AddLogging(l => { l.AddProvider(new NUnitLogger()); })
                        .AddSingleton(_kustoClientProviderMock.Object)
                        .AddSingleton(_kustoClientFactory.Object)
                        .AddSingleton(_historyProviderMock.Object)
                        .BuildServiceProvider();
                    return new TestData(services, (KnownIssuesProvider)services.GetRequiredService<IKnownIssuesService>());
                }
            }

            public static TestData Default()
            {
                return new Builder().Build();
            }

            public static Builder Create() => new Builder();
        }

        [Test]
        public void ReadSavedRecords()
        {
            int buildId = 12345;
            DateTimeOffset stepStartTime = DateTimeOffset.Now.AddMinutes(-1);
            Mock<IDataReader> readerMock = new Mock<IDataReader>();
            readerMock.SetupSequence(x => x.Read())
                .Returns(true)
                .Returns(false);

            readerMock.SetupSequence(x => x.GetInt32(It.IsAny<int>()))
                .Returns(buildId)
                .Returns(1234);

            readerMock.Setup(x => x.IsDBNull(It.IsAny<int>()))
                .Returns(false);

            readerMock.Setup(x => x.GetValue(It.IsAny<int>()))
                .Returns(new object());

            readerMock.SetupSequence(x => x.GetString(It.IsAny<int>()))
                .Returns("BuildRepo")
                .Returns("IssueRepo")
                .Returns("Infrastructure")
                .Returns("12345")
                .Returns("StepName")
                .Returns("SomeURL")
                .Returns("SomePullRequest")
                .Returns("TestProject");

            readerMock.Setup(x => x.GetDateTime(It.IsAny<int>()))
                .Returns(new DateTime(2022,1,1));

            using var testData = TestData.Create().Build();
            List<KnownIssueMatch> savedMatches = KnownIssuesProvider.GetKnownIssueFromDataReader(readerMock.Object);
            savedMatches.Should().HaveCount(1);
            KnownIssueMatch savedMatch = savedMatches.First();
            savedMatch.BuildId.Should().Be(buildId);
            savedMatch.BuildRepository.Should().Be("BuildRepo");
            savedMatch.IssueRepository.Should().Be("IssueRepo");
            savedMatch.IssueId.Should().Be(1234);
            savedMatch.IssueType.Should().Be("Infrastructure");
            savedMatch.JobId.Should().Be("12345");
            savedMatch.StepName.Should().Be("StepName");
            savedMatch.LogURL.Should().Be("SomeURL");
            savedMatch.Project.Should().Be("TestProject");
        }

        [Test]
        public void MapKnownIssueMatchNullStartTime()
        {
            var match = new KnownIssueMatch
            {
                IssueId = 1234,
                BuildId = 9876,
                BuildRepository = "BuildRepo",
                IssueType = "Repo",
                LogURL = null,
                StepStartTime = null
            };
            var kustoMatches = KnownIssuesProvider.MapKnownIssueMatch(match);
            kustoMatches.Should().HaveCount(13);
            kustoMatches[2].StringValue.Should().BeEmpty();
        }
    }
}
