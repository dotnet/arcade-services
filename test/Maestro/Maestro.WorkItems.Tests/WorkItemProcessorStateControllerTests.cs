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

    public bool FailDesiredReads { get; set; }

    public bool FailObservedWrites { get; set; }

    public Task<WorkItemProcessorState?> GetDesiredStateAsync(CancellationToken cancellationToken)
    {
        if (FailDesiredReads)
        {
            throw new InvalidOperationException("Redis is unavailable");
        }

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

    [TestCase(null)]
    [TestCase(WorkItemProcessorState.Working)]
    public async Task StartIsAcknowledgedBeforeAdmissionOpens(WorkItemProcessorState? desiredState)
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = desiredState;

        List<bool> admissionWhenDesiredRead = [];
        _stateStore.OnDesiredStateRead = () => admissionWhenDesiredRead.Add(_admissionGate.IsOpen);

        // Act
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped, WorkItemProcessorState.Working);
        admissionWhenDesiredRead.Should().Equal(false, false);
        _admissionGate.IsOpen.Should().BeTrue();
    }

    [TestCase(null)]
    [TestCase(WorkItemProcessorState.Working)]
    public async Task AdmissionStaysClosedWhenStopIsRequestedBeforeItOpens(WorkItemProcessorState? desiredState)
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = desiredState;
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

    [TestCase(null)]
    [TestCase(WorkItemProcessorState.Working)]
    public async Task AdmissionStaysClosedWhenTheStartCannotBeAcknowledged(WorkItemProcessorState? desiredState)
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = desiredState;
        _stateStore.FailObservedWrites = true;

        // Act
        Func<Task> applyDesiredState = () => controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        await applyDesiredState.Should().ThrowAsync<InvalidOperationException>();
        _admissionGate.IsOpen.Should().BeFalse();
    }

    [TestCase(null)]
    [TestCase(WorkItemProcessorState.Working)]
    public async Task StopIsAcknowledgedOnlyAfterAdmittedWorkFinishes(WorkItemProcessorState? desiredState)
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = desiredState;
        await controller.ApplyDesiredStateAsync(CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WorkItemAdmissionLease lease = await _admissionGate.AdmitWhenOpenAsync(timeout.Token);

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

    [TestCase(null)]
    [TestCase(WorkItemProcessorState.Working)]
    public async Task PendingInitializationKeepsTheReplicaStopped(WorkItemProcessorState? desiredState)
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController(waitForInitialization: true);
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = desiredState;

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

    [TestCase(WorkItemProcessorState.Working)]
    [TestCase(WorkItemProcessorState.Stopped)]
    public async Task MissingDesiredStateMakesTheReplicaWork(WorkItemProcessorState initialDesiredState)
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = initialDesiredState;
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Act
        _stateStore.DesiredState = null;
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        _admissionGate.IsOpen.Should().BeTrue();
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped, WorkItemProcessorState.Working);
    }

    [Test]
    public async Task ExplicitStoppedStateKeepsAdmissionClosed()
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = WorkItemProcessorState.Stopped;

        // Act
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        _admissionGate.IsOpen.Should().BeFalse();
        controller.ObservedState.Should().Be(WorkItemProcessorState.Stopped);
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped);
    }

    [Test]
    public async Task MissingDesiredStateOnConfirmationAllowsAdmissionToOpen()
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.DesiredState = WorkItemProcessorState.Working;
        _stateStore.OnDesiredStateRead = () => _stateStore.DesiredState = null;

        // Act
        await controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        _admissionGate.IsOpen.Should().BeTrue();
        _stateStore.ObservedWrites.Should().Equal(WorkItemProcessorState.Stopped, WorkItemProcessorState.Working);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DesiredStateReadFailureKeepsAdmissionClosed(bool failConfirmationRead)
    {
        // Arrange
        WorkItemProcessorStateController controller = CreateController();
        await controller.ReportStartupStateAsync(CancellationToken.None);
        _stateStore.FailDesiredReads = !failConfirmationRead;
        _stateStore.OnDesiredStateRead = () => _stateStore.FailDesiredReads = true;

        // Act
        Func<Task> applyDesiredState = () => controller.ApplyDesiredStateAsync(CancellationToken.None);

        // Assert
        await applyDesiredState.Should().ThrowAsync<InvalidOperationException>();
        _admissionGate.IsOpen.Should().BeFalse();
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
