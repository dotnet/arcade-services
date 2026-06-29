// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.Tests;

[SetUpFixture]
internal static class SharedData
{
    public static TestDatabase Database { get; private set; } = null!;

    [OneTimeSetUp]
    public static void SetUp()
    {
        Database = new SharedTestDatabase();
    }

    [OneTimeTearDown]
    public static void TearDown()
    {
        Database.Dispose();
        Database = null!;
    }

    private class SharedTestDatabase() : TestDatabase("TestDB_ApiTests_")
    {
    }
}
