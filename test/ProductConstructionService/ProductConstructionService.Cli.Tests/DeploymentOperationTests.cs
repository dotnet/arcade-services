// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Azure;
using Azure.Core;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Azure.ResourceManager.Resources;
using Maestro.WorkItems;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Tools.Cli.Common.Operations;
using Tools.Cli.Common.Options;

namespace ProductConstructionService.Cli.Tests;

public class DeploymentOperationTests
{
    [TestCase(false, true, false)]
    [TestCase(true, true, false)]
    [TestCase(true, null, false)]
    [TestCase(true, false, true)]
    [TestCase(true, false, false, true)]
    [TestCase(true, false, false, false, false)]
    public async Task RunAsync_LeftoverCleanupRequiresStopAndConfirmedDeactivationAsync(
        bool stopConfirmed, bool? activeAfterDeactivation, bool shouldDelete, bool deactivationFails = false, bool replicasGone = true)
    {
        var provider = new Mock<IWorkItemProcessorReplicaProvider>();
        provider.Setup(p => p.GetReplicaNamesAsync("leftover")).ReturnsAsync(["replica"]);
        var store = new Mock<IWorkItemProcessorStateStore>();
        store.Setup(s => s.GetObservedStateAsync("replica", default)).ReturnsAsync(WorkItemProcessorState.Stopped);
        var coordinator = new Mock<ReplicaStateCoordinator>(
            provider.Object, store.Object, NullLogger<ReplicaStateCoordinator>.Instance) { CallBase = true };
        coordinator.Setup(c => c.StopForDeactivationAsync("leftover", default)).ReturnsAsync(stopConfirmed);
        coordinator.Setup(c => c.WaitForReplicasToDisappearAsync("leftover", default)).ReturnsAsync(replicasGone);
        await coordinator.Object.SetDesiredStateAndWaitAsync("leftover", WorkItemProcessorState.Stopped, false);

        var leftover = new Mock<ContainerAppRevisionResource>();
        leftover.SetupGet(r => r.Data).Returns(ArmAppContainersModelFactory.ContainerAppRevisionData(
            name: "leftover", isActive: true, trafficWeight: 0));
        bool deactivated = false;
        leftover.Setup(r => r.DeactivateRevisionAsync(default)).Callback(() => deactivated = true)
            .ReturnsAsync(Mock.Of<Response>());
        if (deactivationFails)
        {
            leftover.Setup(r => r.DeactivateRevisionAsync(default))
                .ThrowsAsync(new InvalidOperationException("Deactivation failed"));
        }
        var refreshed = new Mock<ContainerAppRevisionResource>();
        refreshed.SetupGet(r => r.Data).Returns(ArmAppContainersModelFactory.ContainerAppRevisionData(
            name: "leftover", isActive: activeAfterDeactivation));
        var revisions = new Mock<ContainerAppRevisionCollection>();
        revisions.Setup(r => r.GetAll(null, default)).Returns(Pageable<ContainerAppRevisionResource>.FromPages(
            [Page<ContainerAppRevisionResource>.FromValues([leftover.Object], null, Mock.Of<Response>())]));
        var app = new Mock<ContainerAppResource>();
        app.Setup(a => a.GetContainerAppRevisions()).Returns(revisions.Object);
        app.Setup(a => a.GetContainerAppRevisionAsync("leftover", default))
            .ReturnsAsync(() => Response.FromValue(deactivated ? refreshed.Object : leftover.Object, Mock.Of<Response>()));
        var appData = new ContainerAppData(AzureLocation.WestUS2)
        {
            Configuration = new ContainerAppConfiguration
            {
                Ingress = new ContainerAppIngressConfiguration()
            }
        };
        appData.Configuration.Ingress.Traffic.Add(new ContainerAppRevisionTrafficWeight
        {
            RevisionName = "current", Weight = 100, Label = "blue"
        });
        app.SetupGet(a => a.Data).Returns(appData);
        // A strict process mock prevents any real deployment and records whether cleanup allowed it.
        var processManager = new Mock<IProcessManager>(MockBehavior.Strict);
        var options = new DeploymentOptions
        {
            SubscriptionId = "subscription", ResourceGroupName = "group", ContainerAppName = "app",
            NewImageTag = "tag", ContainerRegistryName = "registry", WorkspaceName = "workspace",
            ImageName = "image", ContainerJobNames = "", AzCliPath = "az", RedisConnectionString = ""
        };
        var operation = new DeploymentOperation(options, processManager.Object, NullLogger<DeploymentOperation>.Instance,
            Mock.Of<ResourceGroupResource>(), coordinator.Object, app.Object);

        var result = await operation.RunAsync();

        result.Should().Be(-1);
        leftover.Verify(r => r.DeactivateRevisionAsync(default), stopConfirmed ? Times.Once : Times.Never);
        store.Verify(s => s.DeleteAsync("replica", default), shouldDelete ? Times.Once : Times.Never);
        coordinator.Verify(c => c.WaitForReplicasToDisappearAsync("leftover", default),
            stopConfirmed && !deactivationFails && activeAfterDeactivation == false ? Times.Once : Times.Never);
        processManager.Invocations.Should().HaveCount(shouldDelete ? 1 : 0);
    }
}
