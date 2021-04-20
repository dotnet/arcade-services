using Microsoft.Extensions.DependencyInjection;
using System;

namespace Microsoft.DotNet.AzureDevOpsTimeline.Tests
{
    public sealed class TestData : IDisposable
    {
        public readonly AzureDevOpsTimeline Controller;
        private readonly ServiceProvider _services;
        public InMemoryTimelineTelemetryRepository Repository { get; }

        public TestData(AzureDevOpsTimeline controller, InMemoryTimelineTelemetryRepository repository, ServiceProvider services)
        {
            Controller = controller;
            Repository = repository;
            _services = services;
        }

        public void Dispose()
        {
            _services?.Dispose();
        }
    }
}
