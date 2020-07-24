// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace Microsoft.DotNet.Internal.Health.Tests
{
    public class HealthReporterFactoryTests
    {
        public abstract class MockProvider : IHealthReportProvider
        {
            public abstract Task UpdateStatusAsync(
                string serviceName,
                string instance,
                string subStatusName,
                HealthStatus status,
                string message);

            public abstract Task<HealthReport> GetStatusAsync(
                string serviceName,
                string instance,
                string subStatusName);

            public abstract Task<IList<HealthReport>> GetAllStatusAsync(string serviceName);
        }

        [Test]
        public async Task ServiceLevelCallsProviderOnceWithCorrectParameters()
        {
            const string testSubStatus = "TEST-SUB-STATUS";
            const string testMessage = "TEST MESSAGE";

            var serviceName = new List<string>();
            var instance = new List<string>();
            var subStatus = new List<string>();
            var status = new List<HealthStatus>();
            var message = new List<string>();
            var provider = new Mock<MockProvider>();
            provider.Setup(
                    p => p.UpdateStatusAsync(
                        Capture.In(serviceName),
                        Capture.In(instance),
                        Capture.In(subStatus),
                        Capture.In(status),
                        Capture.In(message)
                    )
                )
                .Returns(Task.CompletedTask)
                .Verifiable();
            var collection = new ServiceCollection();
            collection.AddHealthReporting(builder => builder.AddProvider(provider.Object));
            await using ServiceProvider services = collection.BuildServiceProvider();
            var reporter = services.GetRequiredService<IServiceHealthReporter<HealthReporterFactoryTests>>();

            await reporter.UpdateStatusAsync(testSubStatus, HealthStatus.Warning, testMessage);
            provider.Verify(
                p => p.UpdateStatusAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<HealthStatus>(),
                    It.IsAny<string>()
                ),
                Times.Once
            );
            provider.VerifyNoOtherCalls();
            serviceName.Should().Equal(typeof(HealthReporterFactoryTests).FullName);
            instance.Should().Equal((string) null);
            subStatus.Should().Equal(testSubStatus);
            status.Should().Equal(HealthStatus.Warning);
            message.Should().Equal(testMessage);
        }

        [Test]
        public async Task MultipleProvidersAreCalled()
        {
            var provider = new Mock<MockProvider>();
            provider.Setup(
                    p => p.UpdateStatusAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<HealthStatus>(),
                        It.IsAny<string>()
                    )
                )
                .Returns(Task.CompletedTask)
                .Verifiable();
            var otherProvider = new Mock<MockProvider>();
            provider.Setup(
                    p => p.UpdateStatusAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<HealthStatus>(),
                        It.IsAny<string>()
                    )
                )
                .Returns(Task.CompletedTask)
                .Verifiable();
            var collection = new ServiceCollection();
            collection.AddHealthReporting(
                builder =>
                {
                    builder.AddProvider(provider.Object);
                    builder.AddProvider(otherProvider.Object);
                }
            );
            await using ServiceProvider services = collection.BuildServiceProvider();
            var reporter = services.GetRequiredService<IServiceHealthReporter<HealthReporterFactoryTests>>();

            await reporter.UpdateStatusAsync("IGNORED", HealthStatus.Warning, "IGNORED");
            provider.Verify(
                p => p.UpdateStatusAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<HealthStatus>(),
                    It.IsAny<string>()
                ),
                Times.Once
            );
            otherProvider.Verify(
                p => p.UpdateStatusAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<HealthStatus>(),
                    It.IsAny<string>()
                ),
                Times.Once
            );
            provider.VerifyNoOtherCalls();
        }

        [Test]
        public async Task InstanceLevelCallsProviderOnceWithCorrectParameters()
        {
            const string testSubStatus = "TEST-SUB-STATUS";
            const string testMessage = "TEST MESSAGE";
            const string testInstance = "TEST-INSTANCE";

            var serviceName = new List<string>();
            var instance = new List<string>();
            var subStatus = new List<string>();
            var status = new List<HealthStatus>();
            var message = new List<string>();

            var provider = new Mock<MockProvider>();
            provider.Setup(
                    p => p.UpdateStatusAsync(
                        Capture.In(serviceName),
                        Capture.In(instance),
                        Capture.In(subStatus),
                        Capture.In(status),
                        Capture.In(message)
                    )
                )
                .Returns(Task.CompletedTask)
                .Verifiable();
            var instanceAccessor = new Mock<IInstanceAccessor>();
            instanceAccessor.Setup(i => i.GetCurrentInstanceName()).Returns(testInstance);
            var collection = new ServiceCollection();
            collection.AddSingleton(instanceAccessor.Object);
            collection.AddHealthReporting(builder => builder.AddProvider(provider.Object));
            await using ServiceProvider services = collection.BuildServiceProvider();
            var reporter = services.GetRequiredService<IInstanceHealthReporter<HealthReporterFactoryTests>>();


            await reporter.UpdateStatusAsync(testSubStatus, HealthStatus.Warning, testMessage);
            provider.Verify(
                p => p.UpdateStatusAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<HealthStatus>(),
                    It.IsAny<string>()
                ),
                Times.Once
            );
            provider.VerifyNoOtherCalls();
            serviceName.Should().Equal(typeof(HealthReporterFactoryTests).FullName);
            instance.Should().Equal(testInstance);
            subStatus.Should().Equal(testSubStatus);
            status.Should().Equal(HealthStatus.Warning);
            message.Should().Equal(testMessage);
        }

        [Test]
        public async Task ExternalServiceCallsProviderOnceWithCorrectParameters()
        {
            const string testSubStatus = "TEST-SUB-STATUS";
            const string testMessage = "TEST MESSAGE";
            const string testInstance = "TEST-INSTANCE";
            const string externalService = "EXTERNAL-SERVICE";

            var serviceName = new List<string>();
            var instance = new List<string>();
            var subStatus = new List<string>();
            var status = new List<HealthStatus>();
            var message = new List<string>();

            var provider = new Mock<MockProvider>();
            provider.Setup(
                    p => p.UpdateStatusAsync(
                        Capture.In(serviceName),
                        Capture.In(instance),
                        Capture.In(subStatus),
                        Capture.In(status),
                        Capture.In(message)
                    )
                )
                .Returns(Task.CompletedTask)
                .Verifiable();
            var instanceAccessor = new Mock<IInstanceAccessor>();
            instanceAccessor.Setup(i => i.GetCurrentInstanceName()).Returns(testInstance);
            var collection = new ServiceCollection();
            collection.AddSingleton(instanceAccessor.Object);
            collection.AddHealthReporting(builder => builder.AddProvider(provider.Object));
            await using ServiceProvider services = collection.BuildServiceProvider();
            var factory = services.GetRequiredService<IHealthReporterFactory>();
            IExternalHealthReporter reporter = factory.ForExternal(externalService);


            await reporter.UpdateStatusAsync(testSubStatus, HealthStatus.Warning, testMessage);
            provider.Verify(
                p => p.UpdateStatusAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<HealthStatus>(),
                    It.IsAny<string>()
                ),
                Times.Once
            );
            provider.VerifyNoOtherCalls();
            serviceName.Should().Equal(externalService);
            instance.Should().Equal((string) null);
            subStatus.Should().Equal(testSubStatus);
            status.Should().Equal(HealthStatus.Warning);
            message.Should().Equal(testMessage);
        }

        [Test]
        public async Task ExternalInstanceCallsProviderOnceWithCorrectParameters()
        {
            const string externalInstance = "EXTERNAL-INSTANCE";
            const string externalService = "EXTERNAL-SERVICE";
            const string testSubStatus = "TEST-SUB-STATUS";
            const string testMessage = "TEST MESSAGE";
            const string testInstance = "TEST-INSTANCE";

            var serviceName = new List<string>();
            var instance = new List<string>();
            var subStatus = new List<string>();
            var status = new List<HealthStatus>();
            var message = new List<string>();

            var provider = new Mock<MockProvider>();
            provider.Setup(
                    p => p.UpdateStatusAsync(
                        Capture.In(serviceName),
                        Capture.In(instance),
                        Capture.In(subStatus),
                        Capture.In(status),
                        Capture.In(message)
                    )
                )
                .Returns(Task.CompletedTask)
                .Verifiable();
            var instanceAccessor = new Mock<IInstanceAccessor>();
            instanceAccessor.Setup(i => i.GetCurrentInstanceName()).Returns(testInstance);
            var collection = new ServiceCollection();
            collection.AddSingleton(instanceAccessor.Object);
            collection.AddHealthReporting(builder => builder.AddProvider(provider.Object));
            await using ServiceProvider services = collection.BuildServiceProvider();
            var factory = services.GetRequiredService<IHealthReporterFactory>();
            IExternalHealthReporter reporter = factory.ForExternalInstance(externalService, externalInstance);

            await reporter.UpdateStatusAsync(testSubStatus, HealthStatus.Warning, testMessage);
            provider.Verify(
                p => p.UpdateStatusAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<HealthStatus>(),
                    It.IsAny<string>()
                ),
                Times.Once
            );
            provider.VerifyNoOtherCalls();
            serviceName.Should().Equal(externalService);
            instance.Should().Equal(externalInstance);
            subStatus.Should().Equal(testSubStatus);
            status.Should().Equal(HealthStatus.Warning);
            message.Should().Equal(testMessage);
        }
    }
}
