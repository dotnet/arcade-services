// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using AwesomeAssertions;
using Maestro.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.Internal.Testing.DependencyInjection.Abstractions;
using Microsoft.DotNet.Internal.Testing.Utility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.Api.v2020_02_20.Controllers;
using ProductConstructionService.Api.v2020_02_20.Models;
using DataModels = Maestro.Data.Models;

namespace ProductConstructionService.Api.Tests;

[TestFixture]
public partial class SubscriptionOutcomesController20200220Tests
{
    private sealed record SeededOutcomes(
        Guid SubscriptionAId,
        Guid SubscriptionBId,
        string OperationIdA1,
        string OperationIdA2,
        string OperationIdB1);

    private static async Task<SeededOutcomes> SeedOutcomesAsync(BuildAssetRegistryContext context)
    {
        // Use unique IDs per call because the test database is shared across tests in this assembly.
        var subscriptionAId = Guid.NewGuid();
        var subscriptionBId = Guid.NewGuid();
        var operationIdA1 = Guid.NewGuid().ToString("N");
        var operationIdA2 = Guid.NewGuid().ToString("N");
        var operationIdB1 = Guid.NewGuid().ToString("N");

        await context.SubscriptionOutcomes.AddRangeAsync(
            new DataModels.SubscriptionOutcome
            {
                OperationId = operationIdA1,
                SubscriptionId = subscriptionAId,
                BuildId = 100,
                Date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Message = "first outcome for A",
                Type = DataModels.SubscriptionOutcomeType.Updated,
            },
            new DataModels.SubscriptionOutcome
            {
                OperationId = operationIdA2,
                SubscriptionId = subscriptionAId,
                BuildId = 101,
                Date = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                Message = "second outcome for A",
                Type = DataModels.SubscriptionOutcomeType.Failure,
            },
            new DataModels.SubscriptionOutcome
            {
                OperationId = operationIdB1,
                SubscriptionId = subscriptionBId,
                BuildId = 200,
                Date = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                Message = "outcome for B",
                Type = DataModels.SubscriptionOutcomeType.Updated,
            });
        await context.SaveChangesAsync();

        return new SeededOutcomes(subscriptionAId, subscriptionBId, operationIdA1, operationIdA2, operationIdB1);
    }

    [Test]
    public async Task ListSubscriptionOutcomes_FiltersBySubscriptionId()
    {
        using TestData data = await TestData.Default.BuildAsync();
        var seeded = await SeedOutcomesAsync(data.Context);

        IActionResult result = await data.SubscriptionOutcomesController.ListSubscriptionOutcomes(
            subscriptionId: seeded.SubscriptionAId.ToString());

        result.Should().BeAssignableTo<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var outcomes = (List<SubscriptionTriggerOutcome>)objResult.Value!;
        outcomes.Should().HaveCount(2);
        outcomes.Should().OnlyContain(o => o.SubscriptionId == seeded.SubscriptionAId);
        // Ordered by date descending: A2 (Feb) before A1 (Jan).
        outcomes.Select(o => o.OperationId)
            .Should().ContainInOrder(seeded.OperationIdA2, seeded.OperationIdA1);
    }

    [Test]
    public async Task ListSubscriptionOutcomes_FiltersByType_AsString()
    {
        using TestData data = await TestData.Default.BuildAsync();
        var seeded = await SeedOutcomesAsync(data.Context);

        IActionResult result = await data.SubscriptionOutcomesController.ListSubscriptionOutcomes(
            subscriptionId: seeded.SubscriptionAId.ToString(),
            subscriptionOutcomeType: "Updated");

        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var outcomes = (List<SubscriptionTriggerOutcome>)objResult.Value!;
        outcomes.Should().ContainSingle()
            .Which.OperationId.Should().Be(seeded.OperationIdA1);
    }

    [Test]
    public async Task ListSubscriptionOutcomes_InvalidType_ReturnsBadRequest()
    {
        using TestData data = await TestData.Default.BuildAsync();

        IActionResult result = await data.SubscriptionOutcomesController.ListSubscriptionOutcomes(
            subscriptionOutcomeType: "NotARealType");

        result.Should().BeAssignableTo<BadRequestObjectResult>();
    }

    [Test]
    public async Task ListSubscriptionOutcomes_InvalidSubscriptionId_ReturnsBadRequest()
    {
        using TestData data = await TestData.Default.BuildAsync();

        IActionResult result = await data.SubscriptionOutcomesController.ListSubscriptionOutcomes(
            subscriptionId: "not-a-guid");

        result.Should().BeAssignableTo<BadRequestObjectResult>();
    }

    [Test]
    public async Task GetSubscriptionOutcome_ReturnsOutcome_WhenFound()
    {
        using TestData data = await TestData.Default.BuildAsync();
        var seeded = await SeedOutcomesAsync(data.Context);

        IActionResult result = await data.SubscriptionOutcomesController.GetSubscriptionOutcome(seeded.OperationIdA1);

        result.Should().BeAssignableTo<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var outcome = (SubscriptionTriggerOutcome)objResult.Value!;
        outcome.OperationId.Should().Be(seeded.OperationIdA1);
        outcome.SubscriptionId.Should().Be(seeded.SubscriptionAId);
        outcome.Type.Should().Be(OutcomeType.Updated);
    }

    [Test]
    public async Task GetSubscriptionOutcome_ReturnsNotFound_WhenMissing()
    {
        using TestData data = await TestData.Default.BuildAsync();

        IActionResult result = await data.SubscriptionOutcomesController.GetSubscriptionOutcome(
            $"missing-{Guid.NewGuid():N}");

        result.Should().BeAssignableTo<NotFoundResult>();
    }

    [Test]
    public async Task GetLatestSubscriptionOutcomes_ReturnsLatestPerSubscription()
    {
        using TestData data = await TestData.Default.BuildAsync();
        var seeded = await SeedOutcomesAsync(data.Context);

        IActionResult result = await data.SubscriptionOutcomesController.GetLatestSubscriptionOutcomes(
            [seeded.SubscriptionAId, seeded.SubscriptionBId]);

        result.Should().BeAssignableTo<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var outcomes = (List<SubscriptionTriggerOutcome>)objResult.Value!;
        outcomes.Should().HaveCount(2);
        // A's latest is the Feb "Failure" outcome (A2), B's only outcome is the Mar one (B1).
        outcomes.Should().ContainSingle(o => o.SubscriptionId == seeded.SubscriptionAId)
            .Which.OperationId.Should().Be(seeded.OperationIdA2);
        outcomes.Should().ContainSingle(o => o.SubscriptionId == seeded.SubscriptionBId)
            .Which.OperationId.Should().Be(seeded.OperationIdB1);
    }

    [Test]
    public async Task GetLatestSubscriptionOutcomes_FiltersToRequestedIds()
    {
        using TestData data = await TestData.Default.BuildAsync();
        var seeded = await SeedOutcomesAsync(data.Context);

        IActionResult result = await data.SubscriptionOutcomesController.GetLatestSubscriptionOutcomes(
            [seeded.SubscriptionAId]);

        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var outcomes = (List<SubscriptionTriggerOutcome>)objResult.Value!;
        outcomes.Should().ContainSingle()
            .Which.SubscriptionId.Should().Be(seeded.SubscriptionAId);
    }

    [Test]
    public async Task GetLatestSubscriptionOutcomes_ReturnsEmpty_WhenNoIdsRequested()
    {
        using TestData data = await TestData.Default.BuildAsync();
        await SeedOutcomesAsync(data.Context);

        IActionResult result = await data.SubscriptionOutcomesController.GetLatestSubscriptionOutcomes([]);

        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var outcomes = (List<SubscriptionTriggerOutcome>)objResult.Value!;
        outcomes.Should().BeEmpty();
    }

    [Test]
    public async Task GetLatestSubscriptionOutcomes_ReturnsEmpty_WhenSubscriptionHasNoOutcomes()
    {
        using TestData data = await TestData.Default.BuildAsync();
        await SeedOutcomesAsync(data.Context);

        IActionResult result = await data.SubscriptionOutcomesController.GetLatestSubscriptionOutcomes(
            [Guid.NewGuid()]);

        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be((int)HttpStatusCode.OK);
        var outcomes = (List<SubscriptionTriggerOutcome>)objResult.Value!;
        outcomes.Should().BeEmpty();
    }

    [TestDependencyInjectionSetup]
    private static class TestDataConfiguration
    {
        public static async Task Dependencies(IServiceCollection collection)
        {
            var connectionString = await SharedData.Database.GetConnectionString();
            collection.AddLogging(l => l.AddProvider(new NUnitLogger()));
            collection.AddSingleton<IHostEnvironment>(new HostingEnvironment
            {
                EnvironmentName = Environments.Development
            });
            collection.AddDbContext<BuildAssetRegistryContext>(options =>
            {
                options.UseSqlServer(connectionString);
                options.EnableServiceProviderCaching(false);
            });
        }

        public static Func<IServiceProvider, BuildAssetRegistryContext> Context(IServiceCollection collection)
        {
            return s => s.GetRequiredService<BuildAssetRegistryContext>();
        }

        public static Func<IServiceProvider, SubscriptionTriggerOutcomesController> SubscriptionOutcomesController(IServiceCollection collection)
        {
            collection.AddSingleton<SubscriptionTriggerOutcomesController>();
            return s => s.GetRequiredService<SubscriptionTriggerOutcomesController>();
        }
    }
}
