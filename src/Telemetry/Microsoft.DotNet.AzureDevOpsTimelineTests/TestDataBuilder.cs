// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.Internal.AzureDevOps;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public class TestDataBuilder
    {
        private readonly ServiceCollection collection = new ServiceCollection();
        private InMemoryTimelineTelemetryRepository _repository = new InMemoryTimelineTelemetryRepository();

        /// <summary>
        /// Create a TestData builder with default configuration
        /// </summary>
        public TestDataBuilder()
        {
            collection.AddOptions();
            collection.AddLogging(logging =>
            {
                logging.AddProvider(new NUnitLogger());
            });
            collection.AddScoped<AzureDevOpsTimeline>();
        }

        public TestData Build()
        {
            collection.AddScoped<AzureDevOpsTimeline>();
            collection.AddSingleton<ITimelineTelemetryRepository>(_repository);

            ServiceProvider services = collection.BuildServiceProvider();
            return new TestData(services.GetRequiredService<AzureDevOpsTimeline>(), _repository, services);
        }

        public TestDataBuilder AddBuildsWithTimelines(IEnumerable<BuildAndTimeline> builds)
        {
            var b = builds.ToDictionary(key => key.Build, key => key.Timelines.ToList());

            collection.AddSingleton<IAzureDevOpsClient>(client => new MockAzureClient(b));

            return this;
        }

        public TestDataBuilder AddTelemetryRepository()
        {
            return this;
        }

        public TestDataBuilder AddTelemetryRepository(string project, DateTimeOffset latestTime)
        {
            _repository = new InMemoryTimelineTelemetryRepository(
                new List<(string project, DateTimeOffset latestTime)> { (project, latestTime) });

            return this;
        }

        public TestDataBuilder AddStaticClock(DateTimeOffset dateTime)
        {
            Mock<ISystemClock> mockSystemClock = new Mock<ISystemClock>();
            mockSystemClock.Setup(x => x.UtcNow).Returns(dateTime);

            collection.AddSingleton(mockSystemClock.Object);

            return this;
        }
    }
}
