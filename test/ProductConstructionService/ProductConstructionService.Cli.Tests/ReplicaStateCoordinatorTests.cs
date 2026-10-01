// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.WorkItems;
using Microsoft.Extensions.Logging.Abstractions;
using Tools.Cli.Common.Operations;

namespace ProductConstructionService.Cli.Tests;

public class ReplicaStateCoordinatorTests
{
    private const string Revision = "revision";
    private const string Replica = "replica";
    private Mock<IWorkItemProcessorReplicaProvider> _provider = null!;
    private Mock<IWorkItemProcessorStateStore> _store = null!;
    private ReplicaStateCoordinator _coordinator = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new();
        _store = new();
        _provider.Setup(p => p.GetReplicaNamesAsync(Revision)).ReturnsAsync([Replica]);
        _coordinator = new(_provider.Object, _store.Object, NullLogger<ReplicaStateCoordinator>.Instance);
    }

    [TestCase(null, true)]
    [TestCase(WorkItemProcessorState.Stopped, false)]
    public async Task StopForDeactivationAsync_StopsBeforeRefreshingEvidenceAndRetainsKeysAsync(
        WorkItemProcessorState? observed, bool neverStarted)
    {
        _store.Setup(s => s.SetDesiredStateAsync(Replica, WorkItemProcessorState.Stopped, default))
            .Returns(Task.CompletedTask);
        _provider.Setup(p => p.GetReplicaStatusesAsync(Revision))
            .Callback(() => _store.Verify(s => s.SetDesiredStateAsync(Replica, WorkItemProcessorState.Stopped, default), Times.Once))
            .ReturnsAsync([new(Replica, neverStarted)]);
        _store.Setup(s => s.GetObservedStateAsync(Replica, default)).ReturnsAsync(observed);

        var result = await _coordinator.StopForDeactivationAsync(Revision);

        result.Should().BeTrue();
        _store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(WorkItemProcessorState.Working, true)]
    [TestCase(WorkItemProcessorState.Working, false)]
    [TestCase(null, false)]
    public async Task StopForDeactivationAsync_RequiresAcknowledgementWithoutSafeEvidenceAsync(
        WorkItemProcessorState? observed, bool neverStarted)
    {
        using var cancellation = new CancellationTokenSource();
        _provider.Setup(p => p.GetReplicaStatusesAsync(Revision)).ReturnsAsync([new(Replica, neverStarted)]);
        _store.Setup(s => s.GetObservedStateAsync(Replica, cancellation.Token))
            .Callback(cancellation.Cancel).ReturnsAsync(observed);

        Func<Task> act = () => _coordinator.StopForDeactivationAsync(Revision, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task StopForDeactivationAsync_RefreshesAppearingReplicaAfterItsStopRequestAsync()
    {
        _provider.SetupSequence(p => p.GetReplicaStatusesAsync(Revision))
            .ReturnsAsync([new(Replica, true), new("new-replica", true)])
            .ReturnsAsync([new(Replica, true), new("new-replica", false)]);
        _store.SetupSequence(s => s.GetObservedStateAsync("new-replica", default))
            .ReturnsAsync((WorkItemProcessorState?)null)
            .ReturnsAsync(WorkItemProcessorState.Stopped);

        var result = await _coordinator.StopForDeactivationAsync(Revision);

        result.Should().BeTrue();
        _provider.Verify(p => p.GetReplicaStatusesAsync(Revision), Times.Exactly(2));
        _store.Verify(s => s.SetDesiredStateAsync("new-replica", WorkItemProcessorState.Stopped, default), Times.Once);
        await _coordinator.DeleteStateAsync(Revision);
        _store.Verify(s => s.DeleteAsync(Replica, default), Times.Once);
        _store.Verify(s => s.DeleteAsync("new-replica", default), Times.Once);
    }

    [Test]
    public async Task StopForDeactivationAsync_DoesNotWaivePreviouslyObservedWorkingAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _provider.SetupSequence(p => p.GetReplicaStatusesAsync(Revision))
            .ReturnsAsync([new(Replica, false), new("new-replica", true)])
            .ReturnsAsync([new(Replica, true)]);
        var reads = 0;
        _store.Setup(s => s.GetObservedStateAsync(Replica, cancellation.Token)).Returns(() =>
        {
            if (++reads == 1)
            {
                return Task.FromResult<WorkItemProcessorState?>(WorkItemProcessorState.Working);
            }

            cancellation.Cancel();
            return Task.FromResult<WorkItemProcessorState?>(null);
        });

        Func<Task> act = () => _coordinator.StopForDeactivationAsync(Revision, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        reads.Should().Be(2);
    }

    [Test]
    public async Task StopForDeactivationAsync_DoesNotWaiveWorkingObservedDuringStartupAsync()
    {
        _store.Setup(s => s.GetObservedStateAsync(Replica, default)).ReturnsAsync(WorkItemProcessorState.Working);
        await _coordinator.SetDesiredStateAndWaitAsync(Revision, WorkItemProcessorState.Working, true);
        using var cancellation = new CancellationTokenSource();
        _provider.Setup(p => p.GetReplicaStatusesAsync(Revision)).ReturnsAsync([new(Replica, true)]);
        _store.Setup(s => s.GetObservedStateAsync(Replica, cancellation.Token))
            .Callback(cancellation.Cancel).ReturnsAsync((WorkItemProcessorState?)null);

        Func<Task> act = () => _coordinator.StopForDeactivationAsync(Revision, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task StopForDeactivationAsync_RejectsWorkerThatStartsDuringRefreshAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _provider.Setup(p => p.GetReplicaStatusesAsync(Revision))
            .Callback(() => _store.Verify(s => s.SetDesiredStateAsync(Replica, WorkItemProcessorState.Stopped, cancellation.Token), Times.Once))
            .ReturnsAsync([new(Replica, false)]);
        _store.Setup(s => s.GetObservedStateAsync(Replica, cancellation.Token))
            .Callback(cancellation.Cancel).ReturnsAsync(WorkItemProcessorState.Working);

        Func<Task> act = () => _coordinator.StopForDeactivationAsync(Revision, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestCase(WorkItemProcessorState.Stopped)]
    [TestCase(WorkItemProcessorState.Working)]
    public async Task SetDesiredStateAndWaitAsync_DoesNotUseLifecycleExceptionAsync(WorkItemProcessorState desired)
    {
        using var cancellation = new CancellationTokenSource();
        _store.Setup(s => s.GetObservedStateAsync(Replica, cancellation.Token))
            .Callback(cancellation.Cancel).ReturnsAsync((WorkItemProcessorState?)null);

        Func<Task> act = () => _coordinator.SetDesiredStateAndWaitAsync(Revision, desired, false, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _provider.Verify(p => p.GetReplicaStatusesAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task WaitForReplicasToDisappearAsync_PreservesStopRequestsWhileReplicasRemainAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _store.Setup(s => s.SetDesiredStateAsync(Replica, WorkItemProcessorState.Stopped, cancellation.Token))
            .Callback(cancellation.Cancel).Returns(Task.CompletedTask);

        Func<Task> act = () => _coordinator.WaitForReplicasToDisappearAsync(Revision, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task WaitForReplicasToDisappearAsync_CompletesOnlyWhenNoReplicasRemainAsync()
    {
        _provider.Setup(p => p.GetReplicaNamesAsync(Revision)).ReturnsAsync([]);

        var result = await _coordinator.WaitForReplicasToDisappearAsync(Revision);

        result.Should().BeTrue();
        _store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task StopForDeactivationAsync_DoesNotTreatInvalidObservedStateAsMissingAsync()
    {
        _provider.Setup(p => p.GetReplicaStatusesAsync(Revision)).ReturnsAsync([new(Replica, true)]);
        _store.Setup(s => s.GetObservedStateAsync(Replica, default)).ThrowsAsync(new InvalidDataException("Invalid state"));

        Func<Task> act = () => _coordinator.StopForDeactivationAsync(Revision);

        await act.Should().ThrowAsync<InvalidDataException>();
        _store.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task StopForDeactivationAsync_AllowsRemovedReplicasButRetainsTheirKeysAsync()
    {
        _provider.Setup(p => p.GetReplicaStatusesAsync(Revision)).ReturnsAsync([]);

        var result = await _coordinator.StopForDeactivationAsync(Revision);

        result.Should().BeTrue();
        _store.Verify(s => s.DeleteAsync(Replica, default), Times.Never);
        await _coordinator.DeleteStateAsync(Revision);
        _store.Verify(s => s.DeleteAsync(Replica, default), Times.Once);
    }
}
