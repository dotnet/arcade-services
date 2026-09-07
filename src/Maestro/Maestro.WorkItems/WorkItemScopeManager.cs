// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maestro.WorkItems;

public class WorkItemScopeManager
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkItemAdmissionGate _admissionGate;

    public WorkItemScopeManager(
        IServiceProvider serviceProvider,
        WorkItemAdmissionGate admissionGate)
    {
        _serviceProvider = serviceProvider;
        _admissionGate = admissionGate;
    }

    /// <summary>
    /// Creates a new scope for the currently executing WorkItem once this replica admits new work.
    /// The admission lease is held until the scope is disposed, so it also covers an empty queue poll.
    /// </summary>
    public async Task<WorkItemScope> BeginWorkItemScopeWhenReadyAsync(CancellationToken cancellationToken)
    {
        WorkItemAdmissionLease lease = await _admissionGate.AdmitWhenOpenAsync(cancellationToken);

        try
        {
            IServiceScope scope = _serviceProvider.CreateScope();

            return new WorkItemScope(
                scope.ServiceProvider.GetRequiredService<IOptions<WorkItemProcessorRegistrations>>(),
                () =>
                {
                    lease.Dispose();
                    return Task.CompletedTask;
                },
                scope);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
