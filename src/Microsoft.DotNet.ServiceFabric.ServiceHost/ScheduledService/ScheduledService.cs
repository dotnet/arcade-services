// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl;
using Quartz.Simpl;

namespace Microsoft.DotNet.ServiceFabric.ServiceHost
{
    internal class ScheduledService<TService>
    {
        private readonly ILogger<ScheduledService<TService>> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public static async Task RunScheduleAsync(ServiceProvider container, CancellationToken cancellationToken)
        {
            var provider = container.GetRequiredService<IServiceProvider>();
            var scheduler = ActivatorUtilities.CreateInstance<ScheduledService<TService>>(provider);
            await scheduler.RunAsync(cancellationToken);
        }

        public ScheduledService(ILogger<ScheduledService<TService>> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        private IEnumerable<(IJobDetail job, ITrigger trigger)> GetCronJobs(CancellationToken cancellationToken)
        {
            Type type = typeof(TService);

            foreach (MethodInfo method in type.GetRuntimeMethods())
            {
                if (method.IsStatic)
                {
                    continue;
                }

                if (method.GetParameters().Length > 1)
                {
                    continue;
                }

                if (method.ReturnType != typeof(Task))
                {
                    continue;
                }

                var attr = method.GetCustomAttribute<CronScheduleAttribute>();
                if (attr == null)
                {
                    continue;
                }

                IJobDetail job = JobBuilder.Create<FuncInvokingJob>()
                    .WithIdentity(method.Name, type.Name)
                    .UsingJobData(new JobDataMap
                    {
                        ["func"] = (Func<Task>) (() => InvokeMethodAsync(method, cancellationToken)),
                    })
                    .Build();

                TimeZoneInfo scheduleTimeZone = TimeZoneInfo.Utc;

                try
                {
                    scheduleTimeZone = TimeZoneInfo.FindSystemTimeZoneById(attr.TimeZone);
                }
                catch (TimeZoneNotFoundException)
                {
                    _logger.LogWarning(
                        "TimeZoneNotFoundException occurred for timezone string: {requestedTimeZoneName}",
                        attr.TimeZone);
                }
                catch (InvalidTimeZoneException)
                {
                    _logger.LogWarning(
                        "InvalidTimeZoneException occurred for timezone string: {requestedTimeZoneName}",
                        attr.TimeZone);
                }

                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity(method.Name + "-Trigger", type.Name)
                    .WithCronSchedule(attr.Schedule, schedule => schedule.InTimeZone(scheduleTimeZone))
                    .StartNow()
                    .Build();

                yield return (job, trigger);
            }
        }

        private async Task InvokeMethodAsync(MethodInfo method, CancellationToken cancellationToken)
        {
            using (_logger.BeginScope("Invoking scheduled method {scheduledMethod}", method.ToString()))
            {
                try
                {
                    using (IServiceScope scope = _scopeFactory.CreateScope())
                    {
                        var impl = scope.ServiceProvider.GetService<TService>();
                        var parameters = method.GetParameters();
                        Task result;
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(CancellationToken))
                        {
                            result = (Task) method.Invoke(impl, new object[] {cancellationToken})!;
                        }
                        else
                        {
                            result = (Task) method.Invoke(impl, Array.Empty<object>())!;
                        }

                        await result;
                    }
                }
                catch (OperationCanceledException ocex) when (ocex.CancellationToken == cancellationToken)
                {
                    //ignore
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception processing scheduled method {scheduledMethod}", method.ToString());
                }
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            IScheduler scheduler = null;
            var name = Guid.NewGuid().ToString();
            try
            {
                DirectSchedulerFactory.Instance.CreateScheduler(name, name, new DefaultThreadPool(), new RAMJobStore());
                scheduler = await DirectSchedulerFactory.Instance.GetScheduler(name, cancellationToken);
                foreach ((IJobDetail job, ITrigger trigger) in GetCronJobs(cancellationToken))
                {
                    await scheduler.ScheduleJob(job, trigger, cancellationToken);
                }

                await scheduler.Start(cancellationToken);
                await cancellationToken.AsTask();
            }
            catch (OperationCanceledException)
            {
                //ignore
            }

            if (scheduler != null)
            {
                await scheduler.Shutdown(true, CancellationToken.None);
            }
        }
    }
}
