// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Data;
using AwesomeAssertions;
using Microsoft.DotNet.Helix.Client;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.DotNet.Kusto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace BuildInsights.BuildAnalysis.Tests.Providers
{
    [TestFixture]
    public partial class HelixDataProviderTests
    {
        [TestDependencyInjectionSetup]
        public static class TestSetup
        {
            public static void Defaults(IServiceCollection collection)
            {
                collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
                collection.AddSingleton(Mock.Of<IHelixApi>());
                collection.Configure<KustoOptions>(
                    o =>
                    {
                        o.Database = "TestDB";
                        o.KustoClusterUri = "ClusterUriString";
                        o.KustoIngestionUri = "IngestionUri";
                        o.ManagedIdentityId = "ManagedIdentityId";
                    }
                );
            }

            public static Func<IServiceProvider, HelixDataProvider> Provider(IServiceCollection collection)
            {
                collection.AddSingleton<HelixDataProvider>();

                return s => s.GetRequiredService<HelixDataProvider>();
            }

            public static Func<IServiceProvider, Mock<IKustoClientProvider>> KustoClientProvider(
                IServiceCollection services, IDataReader reader = null)
            {
                Mock<IKustoClientProvider> kustoClientProviderMock = new Mock<IKustoClientProvider>();
                if (reader != null)
                {

                    kustoClientProviderMock
                        .Setup(m => m.ExecuteKustoQueryAsync(It.IsAny<KustoQuery>()))
                        .ReturnsAsync(reader);
                }

                services.AddSingleton(kustoClientProviderMock.Object);
                return _ => kustoClientProviderMock;
            }
        }


        [TestCase("{\r\n  \"HelixJobId\": \"abc-def-ghi\",\r\n  \"HelixWorkItemName\": \"TestHelixWorkItemName\"\r\n}", true)]
        [TestCase("{\r\n  \"NameA\": \"ValueA\",\r\n  \"NameB\": \"ValueB\"\r\n}", false)]
        public async Task IsHelixWorkItemTest(string comment, bool expectedResult)
        {
            await using TestData testData = await TestData.Default.BuildAsync();
            bool isHelixWorkItemResult = testData.Provider.IsHelixWorkItem(comment);
            isHelixWorkItemResult.Should().Be(expectedResult);
        }

        [Test]
        public async Task TryGetHelixWorkWithAllRecordsInKustoTest()
        {
            string commentA = "{\r\n  \"HelixJobId\": \"abc-def-ghi\",\r\n  \"HelixWorkItemName\": \"TestHelixWorkItemNameCommentA\"\r\n}";
            string commentB = "{\r\n  \"HelixJobId\": \"124-456-789\",\r\n  \"HelixWorkItemName\": \"TestHelixWorkItemNameCommentB\"\r\n}";

            Mock<IDataReader> readerMock = new Mock<IDataReader>();
            readerMock.SetupSequence(x => x.Read())
                .Returns(true)
                .Returns(true)
                .Returns(false);
            readerMock.SetupSequence(x => x.GetString(It.IsAny<int>()))
                .Returns("abc-def-ghi")
                .Returns("TestHelixWorkItemNameCommentA")
                .Returns("ConsoleLogTestingABC")
                .Returns("124-456-789")
                .Returns("TestHelixWorkItemNameCommentB")
                .Returns("ConsoleLogTesting123");
            readerMock.SetupSequence(x => x.GetInt32(It.IsAny<int>()))
                .Returns(0)
                .Returns(0);

            List<string> comments = [commentA, commentB];

            await using TestData testData = await TestData.Default.WithReader(readerMock.Object).BuildAsync();
            var result = await testData.Provider.TryGetHelixWorkItems(comments.ToImmutableList(), CancellationToken.None);
            result[commentA].First().ConsoleLogUrl.Should().Be("ConsoleLogTestingABC");
            result[commentA].First().ExitCode.Should().Be(0);
            result[commentB].First().ConsoleLogUrl.Should().Be("ConsoleLogTesting123");
            result[commentB].First().ExitCode.Should().Be(0);
        }


        [TestCase(8, 1)]
        [TestCase(25, 1)]
        public async Task KustoWorkItemInformationGettingAllRecordsTest(int jobs, int expectedCalls)
        {
            Mock<IDataReader> readerMock = MockReaderResults(jobs);
            await using TestData testData = await TestData.Default.WithReader(readerMock.Object).BuildAsync();
            await testData.Provider.TryGetHelixWorkItems(MockHelixJobsComments(jobs).ToImmutableList(),
                CancellationToken.None);
            testData.KustoClientProvider.Verify(m => m.ExecuteKustoQueryAsync(It.IsAny<KustoQuery>()), Times.Exactly(expectedCalls));
        }

        private List<string> MockHelixJobsComments(int count)
        {
            List<string> helixJobsComments = [];
            for (int i = 0; i < count; i++)
            {
                helixJobsComments.Add($"{{\"HelixJobId\": \"abc-def-ghi-{i}\",\"HelixWorkItemName\": \"TestHelixWorkItemNameComment{i}\"}}");
            }

            return helixJobsComments;
        }


        private Mock<IDataReader> MockReaderResults(int count)
        {
            Mock<IDataReader> readerMock = new Mock<IDataReader>();

            Queue<bool> readerResult = new Queue<bool>();
            Queue<string> workItemResult = new Queue<string>();
            Queue<int> exitCode = new Queue<int>();
            for (int i = 0; i < count; i++)
            {
                readerResult.Enqueue(true);
                workItemResult.Enqueue($"abc-def-ghi-{i}");
                workItemResult.Enqueue($"TestHelixWorkItemNameComment{i}");
                workItemResult.Enqueue("ConsoleLogTestingABC");
                exitCode.Enqueue(0);
            }
            readerResult.Enqueue(false);

            readerMock.Setup(x => x.Read())
                .Returns(readerResult.Dequeue);
            readerMock.Setup(x => x.GetString(It.IsAny<int>()))
                .Returns(workItemResult.Dequeue);
            readerMock.Setup(x => x.GetInt32(It.IsAny<int>()))
                .Returns(exitCode.Dequeue);

            return readerMock;
        }
    }
}

