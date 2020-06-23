// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Fabric;
using System.Linq;
using Castle.DynamicProxy;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceFabric.Services.Remoting;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.V2.FabricTransport.Runtime;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public static class ServiceHostRemoting
    {
        internal static IServiceRemotingListener CreateServiceRemotingListener<TImplementation>(
            ServiceContext context,
            Type[] ifaces,
            IServiceProvider container)
        {
            var client = container.GetRequiredService<TelemetryClient>();
            Type firstIface = ifaces[0];
            Type[] additionalIfaces = ifaces.Skip(1).ToArray();
            var gen = new ProxyGenerator();
            var impl = (IService) gen.CreateInterfaceProxyWithTargetInterface(
                firstIface,
                additionalIfaces,
                (object) null,
                new InvokeInNewScopeInterceptor<TImplementation>(container),
                new LoggingServiceInterceptor(context, client));

            return new FabricTransportServiceRemotingListener(
                context,
                new ActivityServiceRemotingMessageDispatcher(context, impl, null));
        }
    }
}
