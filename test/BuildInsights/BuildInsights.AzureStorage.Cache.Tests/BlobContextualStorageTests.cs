using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using AwesomeAssertions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BuildInsights.AzureStorage.Cache;
using Moq;
using NUnit.Framework;

namespace BuildInsights.AzureStorage.Cache.Tests
{
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public class BlobContextualStorageTests
    {
        internal sealed class TestData : IDisposable, IAsyncDisposable
        {
            private readonly ServiceProvider _services;

            private TestData(
                ServiceProvider services)
            {
                _services = services;
            }

            public BlobContextualStorage BlobContextualStorage => _services.GetRequiredService<BlobContextualStorage>();

            public class Builder
            {
                private readonly Mock<BlobLeaseClient> _mockBlobLeaseClient = new Mock<BlobLeaseClient>();

                public Builder()
                {
                }

                private Builder(Mock<BlobLeaseClient> mockBlobLeaseClient)
                {
                    _mockBlobLeaseClient = mockBlobLeaseClient;
                }

                private Builder With(
                    Mock<BlobLeaseClient> mockBlobLeaseClient)
                {
                    return new Builder(
                        mockBlobLeaseClient ?? _mockBlobLeaseClient);
                }

                public Builder WithBlobLeaseClient(Mock<BlobLeaseClient> mockBlobLeaseClient) => With(mockBlobLeaseClient: mockBlobLeaseClient);

                public TestData Build()
                {
                    var collection = new ServiceCollection();
                    collection.AddOptions();
                    collection.Configure<BlobStorageSettings>(o =>
                    {
                        o.Endpoint = "FAKE-CONNECTION-STRING";
                        o.ContainerName = "FakeContainerName";
                        o.LeaseRenewalTimespan = TimeSpan.FromMilliseconds(1);
                        o.LeaseAcquireRetryWaitTime = TimeSpan.FromSeconds(2);
                    });

                    Mock<BlobClient> mockBlobClient = new Mock<BlobClient>();

                    Mock<BlobContainerClient> mockBlobContainerClient = new Mock<BlobContainerClient>();

                    Mock<IBlobClientFactory> mockBlobClientFactory = new Mock<IBlobClientFactory>();
                    mockBlobClientFactory.Setup(m => m.CreateBlobClient(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(mockBlobClient.Object);
                    mockBlobClientFactory.Setup(m => m.CreateBlobLeaseClient(mockBlobClient.Object, It.IsAny<string>())).Returns(_mockBlobLeaseClient.Object);
                    mockBlobClientFactory.Setup(m => m.CreateBlobContainerClient(It.IsAny<string>(), It.IsAny<string>())).Returns(mockBlobContainerClient.Object);
                    
                    collection.AddSingleton(mockBlobClientFactory.Object);
                    collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
                    collection.AddScoped<BlobContextualStorage>();

                    ServiceProvider service = collection.BuildServiceProvider();
                    return new TestData(service);
                }
            }

            public static Builder Create() => new Builder();
            public static TestData BuildDefault() => Create().Build();

            public void Dispose()
            {
                _services.Dispose();
            }

            public ValueTask DisposeAsync()
            {
                return _services.DisposeAsync();
            }
        }

        [Test]
        public async Task ValidateAutoRenewalRunsAsync()
        {
            Mock<BlobLeaseClient> mockBlobLeaseClient = new Mock<BlobLeaseClient>();
            mockBlobLeaseClient.Setup(m => m.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).Returns(GetDefaultResponse()).Verifiable();
            mockBlobLeaseClient.Setup(m => m.RenewAsync(It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).Returns(GetDefaultResponse()).Verifiable();

            var sut = TestData.Create().WithBlobLeaseClient(mockBlobLeaseClient).Build();
            using var lockObj = await sut.BlobContextualStorage.AcquireAsync("FakeLockName", TimeSpan.FromSeconds(1), CancellationToken.None);

            await Task.Delay(TimeSpan.FromSeconds(1));

            mockBlobLeaseClient.Verify();
        }

        [Test]
        public async Task ValidateMultipleSameLockAcquiresAsync()
        {
            Mock<BlobLeaseClient> mockBlobLeaseClient = new Mock<BlobLeaseClient>();
            mockBlobLeaseClient.Setup(m => m.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).Returns(GetDefaultResponse());
            mockBlobLeaseClient.Setup(m => m.RenewAsync(It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).Returns(GetDefaultResponse());

            var sut = TestData.Create().WithBlobLeaseClient(mockBlobLeaseClient).Build();
            using var lockObj = await sut.BlobContextualStorage.AcquireAsync("FakeLockName", TimeSpan.FromSeconds(1), CancellationToken.None);

            await sut.BlobContextualStorage.Invoking(o => o.AcquireAsync("FakeLockName", TimeSpan.FromSeconds(1), CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();
        }

        [Test]
        public async Task ValidateRetryAcquireAsync()
        {
            Mock<BlobLeaseClient> mockBlobLeaseClient = new Mock<BlobLeaseClient>();
            mockBlobLeaseClient.SetupSequence(m => m.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException(409, string.Empty, "LeaseAlreadyPresent", null))
                .Returns(GetDefaultResponse());
            mockBlobLeaseClient.Setup(m => m.RenewAsync(It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).Returns(GetDefaultResponse());

            var sut = TestData.Create().WithBlobLeaseClient(mockBlobLeaseClient).Build();
            using var lockObj = await sut.BlobContextualStorage.AcquireAsync("FakeLockName", TimeSpan.FromSeconds(1), CancellationToken.None);

            mockBlobLeaseClient.Verify(m => m.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }


        [Test]
        public async Task ValidateRetryTimeoutAcquireAsync()
        {
            Mock<BlobLeaseClient> mockBlobLeaseClient = new Mock<BlobLeaseClient>();
            mockBlobLeaseClient.Setup(m => m.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>()))
                .Throws(new RequestFailedException(409, string.Empty, "LeaseAlreadyPresent", null));
            mockBlobLeaseClient.Setup(m => m.RenewAsync(It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).Returns(GetDefaultResponse());

            var sut = TestData.Create().WithBlobLeaseClient(mockBlobLeaseClient).Build();
            await sut.BlobContextualStorage.Invoking(o => o.AcquireAsync("FakeLockName", TimeSpan.FromSeconds(1), CancellationToken.None)).Should().ThrowAsync<TimeoutException>();
        }

        [Test]
        public async Task ValidateDisposalOfLease()
        {
            Mock<BlobLeaseClient> mockBlobLeaseClient = new Mock<BlobLeaseClient>();
            mockBlobLeaseClient.Setup(m => m.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).Returns(GetDefaultResponse()).Verifiable();
            mockBlobLeaseClient.Setup(m => m.ReleaseAsync(It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>())).Returns(GetReleaseDefaultResponse()).Verifiable();

            var sut = TestData.Create().WithBlobLeaseClient(mockBlobLeaseClient).Build();
            Task autoRenewalTask;
            using (var lockObj = await sut.BlobContextualStorage.AcquireAsync("FakeLockName", TimeSpan.FromSeconds(1), CancellationToken.None))
            {
                mockBlobLeaseClient.Verify(m => m.ReleaseAsync(It.IsAny<RequestConditions>(), It.IsAny<CancellationToken>()), Times.Never);

                var autoRenewalTaskProperty = lockObj!.GetType().GetProperty("AutoRenewalTask");
                MethodInfo strGetter = autoRenewalTaskProperty!.GetGetMethod(nonPublic: true)!;
                autoRenewalTask = (Task)strGetter.Invoke(lockObj, null)!;
            }

            mockBlobLeaseClient.Verify();
            autoRenewalTask.Status.Should().Be(TaskStatus.RanToCompletion);
        }

        private Task<Response<BlobLease>> GetDefaultResponse()
        {
            Mock<Response> responseMock = new Mock<Response>();
            responseMock.SetupGet(m => m.Status).Returns(200);

            BlobLease fakeBlobLease = BlobsModelFactory.BlobLease(new ETag(), new DateTimeOffset(), "FakeLeaseId");
            Response<BlobLease> response = Response.FromValue(fakeBlobLease, responseMock.Object);

            return Task.FromResult(response);
        }

        private Task<Response<ReleasedObjectInfo>> GetReleaseDefaultResponse()
        {
            Mock<Response> responseMock = new Mock<Response>();
            responseMock.SetupGet(m => m.Status).Returns(200);

            ReleasedObjectInfo fakeReleasedObjectInfo = new ReleasedObjectInfo(new ETag(), new DateTimeOffset());
            Response<ReleasedObjectInfo> response = Response.FromValue(fakeReleasedObjectInfo, responseMock.Object);

            return Task.FromResult(response);
        }
    }
}
