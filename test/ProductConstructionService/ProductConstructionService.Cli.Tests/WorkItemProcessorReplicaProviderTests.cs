// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Azure;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Maestro.WorkItems;

namespace ProductConstructionService.Cli.Tests;

public class WorkItemProcessorReplicaProviderTests
{
    [TestCase("", 0, false, false, "Waiting", true)]
    [TestCase(null, 0, false, false, "Waiting", true)]
    [TestCase("container-id", 0, false, false, "Waiting", false)]
    [TestCase("", 1, false, false, "Waiting", false)]
    [TestCase("", null, false, false, "Waiting", false)]
    [TestCase("", 0, true, false, "Waiting", false)]
    [TestCase("", 0, false, true, "Waiting", false)]
    [TestCase("", 0, null, false, "Waiting", false)]
    [TestCase("", 0, false, null, "Waiting", false)]
    [TestCase("", 0, false, false, "Running", false)]
    [TestCase("container-id", 0, true, false, "Running", false)]
    [TestCase("", 0, false, false, "Terminated", false)]
    [TestCase("", 0, false, false, null, false)]
    [TestCase("", 0, false, false, "Unknown", false)]
    public async Task GetReplicaStatusesAsync_RequiresConsistentNeverStartedEvidenceAsync(
        string? containerId, int? restarts, bool? started, bool? ready, string? runningState, bool expected)
    {
        var container = ArmAppContainersModelFactory.ContainerAppReplicaContainer(
            name: "worker", containerId: containerId, restartCount: restarts, isStarted: started, isReady: ready,
            runningState: runningState is null ? (ContainerAppContainerRunningState?)null : new ContainerAppContainerRunningState(runningState));
        var provider = CreateProvider([container], ["worker"]);

        var statuses = await provider.GetReplicaStatusesAsync("revision");

        statuses.Should().ContainSingle().Which.Should().Be(new WorkItemProcessorReplicaStatus("replica", expected));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public async Task GetReplicaStatusesAsync_RejectsMissingContainerMetadataAsync(bool includeContainer, bool includeTemplate)
    {
        var container = ArmAppContainersModelFactory.ContainerAppReplicaContainer(
            name: "worker", restartCount: 0, isStarted: false, isReady: false,
            runningState: ContainerAppContainerRunningState.Waiting);
        var provider = CreateProvider(includeContainer ? [container] : [], includeTemplate ? ["worker"] : null);

        var statuses = await provider.GetReplicaStatusesAsync("revision");

        statuses.Should().ContainSingle().Which.HasNeverStarted.Should().BeFalse();
    }

    [Test]
    public async Task GetReplicaStatusesAsync_RequiresAllExpectedContainersAsync()
    {
        var container = ArmAppContainersModelFactory.ContainerAppReplicaContainer(
            name: "sidecar", restartCount: 0, isStarted: false, isReady: false,
            runningState: ContainerAppContainerRunningState.Waiting);
        var provider = CreateProvider([container], ["worker", "sidecar"]);

        var statuses = await provider.GetReplicaStatusesAsync("revision");

        statuses.Should().ContainSingle().Which.HasNeverStarted.Should().BeFalse();
    }

    private static ContainerAppWorkItemProcessorReplicaProvider CreateProvider(
        ContainerAppReplicaContainer[] containers, string[]? expectedContainers)
    {
        var replica = new Mock<ContainerAppReplicaResource>();
        replica.SetupGet(r => r.Data).Returns(ArmAppContainersModelFactory.ContainerAppReplicaData(name: "replica", containers: containers));
        var replicas = new Mock<ContainerAppReplicaCollection>();
        replicas.Setup(r => r.GetAll(default)).Returns(Pageable<ContainerAppReplicaResource>.FromPages(
            [Page<ContainerAppReplicaResource>.FromValues([replica.Object], null, Mock.Of<Response>())]));

        ContainerAppTemplate? template = null;
        if (expectedContainers is not null)
        {
            template = new ContainerAppTemplate();
            foreach (var name in expectedContainers)
            {
                template.Containers.Add(new ContainerAppContainer { Name = name });
            }
        }

        var revision = new Mock<ContainerAppRevisionResource>();
        revision.SetupGet(r => r.Data).Returns(ArmAppContainersModelFactory.ContainerAppRevisionData(template: template));
        revision.Setup(r => r.GetContainerAppReplicas()).Returns(replicas.Object);
        var app = new Mock<ContainerAppResource>();
        app.Setup(a => a.GetContainerAppRevisionAsync("revision", default))
            .ReturnsAsync(Response.FromValue(revision.Object, Mock.Of<Response>()));
        return new(app.Object);
    }
}
