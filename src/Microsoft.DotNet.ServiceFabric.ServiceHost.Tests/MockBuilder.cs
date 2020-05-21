using System;
using System.Fabric;
using Moq;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost.Tests
{
    public static class MockBuilder
    {
        public static StatelessServiceContext StatelessServiceContext()
        {
            return new StatelessServiceContext(
                new NodeContext("IGNORED", new NodeId(1, 1), 1, "IGNORED", "IGNORED.test"),
                Mock.Of<ICodePackageActivationContext>(),
                "TestService",
                new Uri("service://TestName"),
                new byte[0],
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                1);
        }
        public static StatefulServiceContext StatefulServiceContext()
        {
            return new StatefulServiceContext(
                new NodeContext("IGNORED", new NodeId(1, 1), 1, "IGNORED", "IGNORED.test"),
                Mock.Of<ICodePackageActivationContext>(),
                "TestService",
                new Uri("service://TestName"),
                new byte[0],
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                1);
        }
    }
}