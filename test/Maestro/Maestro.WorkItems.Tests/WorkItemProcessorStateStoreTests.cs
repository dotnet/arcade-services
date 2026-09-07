// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.WorkItems;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maestro.WorkItem.Tests;

public class WorkItemProcessorStateStoreTests
{
    private const string ReplicaName = "testReplica";

    private FakeRedisCacheFactory _cacheFactory = null!;
    private WorkItemProcessorStateStore _store = null!;

    [SetUp]
    public void TestSetup()
    {
        _cacheFactory = new FakeRedisCacheFactory();
        _store = new WorkItemProcessorStateStore(_cacheFactory, NullLogger<WorkItemProcessorStateStore>.Instance);
    }

    [Test]
    public async Task DesiredAndObservedStatesUseSeparateKeys()
    {
        // Arrange
        // Act
        await _store.SetDesiredStateAsync(ReplicaName, WorkItemProcessorState.Working, CancellationToken.None);
        await _store.SetObservedStateAsync(ReplicaName, WorkItemProcessorState.Stopped, CancellationToken.None);

        // Assert
        _cacheFactory.Store.Keys.Should().BeEquivalentTo(
        [
            $"queue-processing-state:{ReplicaName}:desired",
            $"queue-processing-state:{ReplicaName}:observed",
        ]);
        (await _store.GetDesiredStateAsync(ReplicaName, CancellationToken.None)).Should().Be(WorkItemProcessorState.Working);
        (await _store.GetObservedStateAsync(ReplicaName, CancellationToken.None)).Should().Be(WorkItemProcessorState.Stopped);
    }

    [Test]
    public async Task MissingStateIsReportedAsNull()
    {
        // Arrange
        // Act
        WorkItemProcessorState? desiredState = await _store.GetDesiredStateAsync(ReplicaName, CancellationToken.None);
        WorkItemProcessorState? observedState = await _store.GetObservedStateAsync(ReplicaName, CancellationToken.None);

        // Assert
        desiredState.Should().BeNull();
        observedState.Should().BeNull();
    }

    [Test]
    public async Task UnrecognizedStateIsReportedAsNull()
    {
        // Arrange
        _cacheFactory.Store[$"queue-processing-state:{ReplicaName}:desired"] = "Stopping";

        // Act
        WorkItemProcessorState? desiredState = await _store.GetDesiredStateAsync(ReplicaName, CancellationToken.None);

        // Assert
        desiredState.Should().BeNull();
    }

    [Test]
    public async Task DeleteRemovesBothKeys()
    {
        // Arrange
        await _store.SetDesiredStateAsync(ReplicaName, WorkItemProcessorState.Working, CancellationToken.None);
        await _store.SetObservedStateAsync(ReplicaName, WorkItemProcessorState.Working, CancellationToken.None);

        // Act
        await _store.DeleteAsync(ReplicaName, CancellationToken.None);

        // Assert
        _cacheFactory.Store.Should().BeEmpty();
    }

    [Test]
    public async Task ReplicaStoreOnlyReadsDesiredAndWritesObserved()
    {
        // Arrange
        var replicaStore = new ReplicaWorkItemProcessorStateStore(_store, new WorkItemProcessorReplicaName(ReplicaName));
        await _store.SetDesiredStateAsync(ReplicaName, WorkItemProcessorState.Working, CancellationToken.None);

        // Act
        WorkItemProcessorState? desiredState = await replicaStore.GetDesiredStateAsync(CancellationToken.None);
        await replicaStore.SetObservedStateAsync(WorkItemProcessorState.Working, CancellationToken.None);

        // Assert
        desiredState.Should().Be(WorkItemProcessorState.Working);
        _cacheFactory.Store[$"queue-processing-state:{ReplicaName}:desired"].Should().Be("Working");
        _cacheFactory.Store[$"queue-processing-state:{ReplicaName}:observed"].Should().Be("Working");
    }
}
