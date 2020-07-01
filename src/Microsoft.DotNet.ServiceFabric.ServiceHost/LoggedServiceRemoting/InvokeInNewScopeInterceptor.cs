using System;
using System.Fabric;
using System.Threading.Tasks;
using Castle.DynamicProxy;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    public class InvokeInNewScopeInterceptor<TService> : AsyncInterceptor
    {
        private readonly IServiceProvider _outerScope;

        public InvokeInNewScopeInterceptor(IServiceProvider outerScope){
            _outerScope = outerScope;
        }

        protected override async Task<T> InterceptAsync<T>(IInvocation invocation, Func<Task<T>> call)
        {
            using (IServiceScope scope = _outerScope.CreateScope())
            {
                var client = scope.ServiceProvider.GetRequiredService<TelemetryClient>();
                var context = scope.ServiceProvider.GetRequiredService<ServiceContext>();
                string url =
                    $"{context.ServiceName}/{invocation.Method?.DeclaringType?.Name}/{invocation.Method?.Name}";
                using (IOperationHolder<RequestTelemetry> op = client.StartOperation<RequestTelemetry>($"RPC {url}"))
                {
                    try
                    {
                        op.Telemetry.Url = new Uri(url);
                        
                        var instance = scope.ServiceProvider.GetRequiredService<TService>();
                        ((IChangeProxyTarget) invocation).ChangeInvocationTarget(instance);
                        return await call();
                    }
                    catch (Exception ex)
                    {
                        op.Telemetry.Success = false;
                        client.TrackException(ex);
                        throw;
                    }
                }
            }
        }
    }
}
