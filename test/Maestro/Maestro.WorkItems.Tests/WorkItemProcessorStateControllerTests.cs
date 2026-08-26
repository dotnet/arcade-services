// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.WorkItems;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maestro.WorkItem.Tests;

internal class FakeReplicaStateStore : IReplicaWorkItemProcessorStateStore
{
    public WorkItemProcessorState? DesiredState { get; set; }

    public List<WorkItemProcessorState> ObservedWrites { get; } = [];

    public Action? OnDesiredStateRead { get; set; }

    public bool FailObservedWrites { get; set; }

    public Task<WorkItemProcessorState?> GetDesiredStateAsync(CancellationToken cancellationToken)
    {
        WorkItemProcessorState? state = DesiredState;
        OnDesiredStateRead?.Invoke();
        return Task.FromResult(state);
    }

    public Task SetObservedStateAsync(WorkItemProcessorState state, CancellationToken cancellationToken)
    {
        if (FailObservedWrites)
        {
            throw new InvalidOperationException("Redis is unavailable");
        }

        ObservedWrites.Add(state);
        return Task.CompletedTask;
    }
}

public class WorkItemProcessorStateControllerTests
{
    private FakeReplicaStateStore _stateStore = null!;
    private WorkItemAdmissionGate _admissionGate = null!;

    [SetUp]
    public void TestSetup()
    {
        _stateStore = new FakeReplicaStateStore();
        _admissionGate = new WorkItemAdmissionGate();
    }

    [Test]
    public async Task ReplicaStartsStoppedWithClosedAdmission()
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();

        // Act
        await controller.ReportStartupStateAsync(CancellationToken.None);

        // Assert
        _admissionGate.IsOpen.Should().BeFalse();
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped);
    }

    [Test]
    public async Task StartIsAcknowledgedBeforeAdmissionOpens()
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = WorkItemProcessorState.Working;

        List<bool> admissionWhenDesiredRead = [];
        _stateStore.OnDesiredStateRead = () => admissionWhenDesiredRead.Add(_admissionGate.IsOpen);

        // Act
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped, WorkItemProcessorState.Working);
        admissionWhenDesiredRead.Should().Equal(false, false);
        _admissionGate.IsOpen.Should().BeTrue();
    }

    [Test]
    public async Task AdmissionStaysClosedWhenStopIsRequestedBeforeItOpens()
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = WorkItemProcessorState.Working;
        _stateStore.OnDesiredStateRead = () => _stateStore.DesiredState = WorkItemProcessorState.Stopped;

        // Act
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        _admissionGate.IsOpen.Should().BeFalse();
        _stateStore.ObservedWrites.Should().Equal(
            WorkItemProcessorState.Stopped,
            WorkItemProcessorState.Working,
            WorkItemProcessorState.Stopped);
    }

    [Test]
    public async Task AdmissionStaysClosedWhenTheStartCannotBeAcknowledged()
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = WorkItemProcessorState.Working;
        _stateStore.FailObservedWrites = true;

        // Act
        Func<Task> applyDesiredState = () => controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        await applyDesiredState.Should().ThrowAsync<InvalidOperationException>();
        _admissionGate.IsOpen.Should().BeFalse();
    }

    [Test]
    public async Task StopIsAcknowledgedOnlyAfterAdmittedWorkFinishes()
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = WorkItemProcessorState.Working;
        await controller.ApplyDesiredStateAsync(CancellationToken.None);
        WorkItemAdmissionLease lease = await _admissionGate.AdmitWhenOpenAsync(CancellationToken.None);

        // Act
        _stateStore.DesiredState = WorkItemProcessorState.Stopped;
        Task applyStop = controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        _admissionGate.IsOpen.Should().BeFalse();
        applyStop.IsCompleted.Should().BeFalse();
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped, WorkItemProcessorState.Working);

        lease.Dispose();
        await applyStop.WaitAsync(TimeSpan.FromSeconds(5));
        _stateStore.ObservedWrites.Should().Equal(
            WorkItemProcessorState.Stopped,
            WorkItemProcessorState.Working,
            WorkItemProcessorState.Stopped);
    }

    [Test]
    public async Task PendingInitializationKeepsTheReplicaStopped()
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController(waitForInitialization: true);
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = WorkItemProcessorState.Working;

        // Act
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        controller.IsInitializationPending.Should().BeTrue();
        _admissionGate.IsOpen.Should().BeFalse();
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped);

        controller.InitializationFinished();
        await controller.ApplyDesiredStateAsync(CancellationToken.None);
        _admissionGate.IsOpen.Should().BeTrue();
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped, WorkItemProcessorState.Working);
    }

    [Test]
    public async Task MissingDesiredStateKeepsTheCurrentLocalState()
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = WorkItemProcessorState.Working;
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Act
        _stateStore.DesiredState = null;
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        _admissionGate.IsOpen.Should().BeTrue();
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped, WorkItemProcessorState.Working);
    }

    private WorkItemProcessorStateController CreateController(bool waitForInitialization = false)
    {
        return new WorkItemProcessorStateController(
            _stateStore,
            _admissionGate,
            new WorkItemProcessorStateControllerOptions(waitForInitialization, TimeSpan.FromMilliseconds(1)),
            NullLogger<WorkItemProcessorStateController>.Instance);
    }
}
