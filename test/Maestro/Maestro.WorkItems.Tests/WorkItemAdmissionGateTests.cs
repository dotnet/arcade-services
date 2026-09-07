// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.WorkItems;

namespace Maestro.WorkItem.Tests;

public class WorkItemAdmissionGateTests
{
    [Test]
    public void ClosedGateDoesNotAdmitWork()
    {
        // Arrange
        WorkItemAdmissionGate gate = new();

        // Act
        Task<WorkItemAdmissionLease> admission = gate.AdmitWhenOpenAsync(CancellationToken.None);

        // Assert
        admission.IsCompleted.Should().BeFalse();
        gate.ActiveAdmissionCount.Should().Be(0);
    }

    [Test]
    public async Task OpeningTheGateReleasesWaitingConsumers()
    {
        // Arrange
        WorkItemAdmissionGate gate = new();
        Task<WorkItemAdmissionLease> admission = gate.AdmitWhenOpenAsync(CancellationToken.None);

        // Act
        gate.Open();

        // Assert
        using WorkItemAdmissionLease lease = await admission.WaitAsync(TimeSpan.FromSeconds(5));
        gate.ActiveAdmissionCount.Should().Be(1);
    }

    [Test]
    public async Task DrainCompletesOnlyAfterAdmittedWorkFinishes()
    {
        // Arrange
        WorkItemAdmissionGate gate = new();
        gate.Open();
        WorkItemAdmissionLease lease = await gate.AdmitWhenOpenAsync(CancellationToken.None);

        // Act
        gate.Close();
        Task drained = gate.WaitUntilDrainedAsync(CancellationToken.None);

        // Assert
        drained.IsCompleted.Should().BeFalse();
        lease.Dispose();
        await drained.WaitAsync(TimeSpan.FromSeconds(5));
        gate.ActiveAdmissionCount.Should().Be(0);
    }

    [Test]
    public async Task ClosedGateDoesNotAdmitWorkAfterAPreviousAdmission()
    {
        // Arrange
        WorkItemAdmissionGate gate = new();
        gate.Open();
        using WorkItemAdmissionLease lease = await gate.AdmitWhenOpenAsync(CancellationToken.None);

        // Act
        gate.Close();
        Task<WorkItemAdmissionLease> secondAdmission = gate.AdmitWhenOpenAsync(CancellationToken.None);

        // Assert
        secondAdmission.IsCompleted.Should().BeFalse();
        gate.ActiveAdmissionCount.Should().Be(1);
    }

    [Test]
    public async Task DrainOfAnIdleGateCompletesImmediately()
    {
        // Arrange
        WorkItemAdmissionGate gate = new();

        // Act
        gate.Close();

        // Assert
        await gate.WaitUntilDrainedAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }
}
