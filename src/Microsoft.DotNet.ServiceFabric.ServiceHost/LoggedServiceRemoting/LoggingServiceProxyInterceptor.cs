// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Fabric;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class LoggingServiceProxyInterceptor : AsyncInterceptor
    {
        public LoggingServiceProxyInterceptor(
            TelemetryClient telemetryClient,
            ServiceContext context,
            string serviceUri)
        {
            TelemetryClient = telemetryClient;
            Context = context;
            ServiceUri = serviceUri;
        }

        private TelemetryClient TelemetryClient { get; }
        private ServiceContext Context { get; }
        private string ServiceUri { get; }
        
        protected override async Task<T> InterceptAsync<T>(IInvocation invocation, Func<Task<T>> call)
        {
            string methodName = $"/{invocation.Method?.DeclaringType?.Name}/{invocation.Method?.Name}";
            using (IOperationHolder<DependencyTelemetry> op =
                TelemetryClient.StartOperation<DependencyTelemetry>($"RPC {ServiceUri}{methodName}"))
            {
                try
                {
                    Activity.Current.AddBaggage("CallingServiceName", $"\"{Context.ServiceName}\"");
                    op.Telemetry.Type = "ServiceFabricRemoting";
                    op.Telemetry.Target = ServiceUri;
                    op.Telemetry.Data = ServiceUri + methodName;
                    return await call();
                }
                catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
                {
                    Exception primaryException = ex.InnerExceptions[0];

                    op.Telemetry.Success = false;
                    TelemetryClient.TrackException(primaryException);
                    ExceptionDispatchInfo.Capture(primaryException).Throw();
                    // throw; is Required by the compiler because it doesn't know that ExceptionDispatchInfo.Throw throws
                    // ReSharper disable once HeuristicUnreachableCode
                    throw;
                }
                catch (Exception ex)
                {
                    op.Telemetry.Success = false;
                    TelemetryClient.TrackException(ex);
                    throw;
                }
            }
        }
    }
}
