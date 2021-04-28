// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

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
