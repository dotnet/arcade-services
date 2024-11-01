// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maestro.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using NUnit.Framework;

namespace Maestro.Web.Tests;

[SetUpFixture]
public static class SharedData
{
    public static TestDatabase Database { get; private set; }

    [OneTimeSetUp]
    public static void SetUp()
    {
        Database = new SharedTestDatabase();
    }

    [OneTimeTearDown]
    public static void TearDown()
    {
        Database.Dispose();
        Database = null;
    }

    private class SharedTestDatabase : TestDatabase
    {
    }
}

public class TestDatabase : IDisposable
{
    private const string TestDatabasePrefix = "TFD_";
    private readonly Lazy<Task<string>> _databaseName = new(
        InitializeDatabaseAsync,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public void Dispose()
    {
        using var connection = new SqlConnection(BuildAssetRegistryContextFactory.GetConnectionString("master"));
        connection.Open();
        DropAllTestDatabases(connection).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async Task<string> GetConnectionString() => BuildAssetRegistryContextFactory.GetConnectionString(await _databaseName.Value);

    private static async Task<string> InitializeDatabaseAsync()
    {
        string databaseName = TestDatabasePrefix +
            $"_{TestContext.CurrentContext.Test.ClassName!.Split('.').Last()}" +
            $"_{TestContext.CurrentContext.Test.MethodName}" +
            $"_{DateTime.Now:yyyyMMddHHmmss}";

        TestContext.WriteLine($"Creating database '{databaseName}'");

        await using (var connection = new SqlConnection(BuildAssetRegistryContextFactory.GetConnectionString("master")))
        {
            await connection.OpenAsync();
            await DropAllTestDatabases(connection);
            await using (SqlCommand createCommand = connection.CreateCommand())
            {
                createCommand.CommandText = $"CREATE DATABASE {databaseName}";
                await createCommand.ExecuteNonQueryAsync();
            }
        }

        var collection = new ServiceCollection();
        collection.AddSingleton<IHostEnvironment>(new HostingEnvironment
        {
            EnvironmentName = Environments.Development
        });
        collection.AddBuildAssetRegistry(o =>
        {
            o.UseSqlServer(BuildAssetRegistryContextFactory.GetConnectionString(databaseName));
            o.EnableServiceProviderCaching(false);
        });

        await using ServiceProvider provider = collection.BuildServiceProvider();
        await provider.GetRequiredService<BuildAssetRegistryContext>().Database.MigrateAsync();

        return databaseName;
    }

    private static async Task DropAllTestDatabases(SqlConnection connection)
    {
        var previousTestDbs = new List<string>();
        await using (SqlCommand command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT name FROM sys.databases WHERE name LIKE '{TestDatabasePrefix}%'";
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                previousTestDbs.Add(reader.GetString(0));
            }
        }

        foreach (string db in previousTestDbs)
        {
            TestContext.WriteLine($"Dropping test database '{db}'");
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE {db} SET single_user with rollback immediate; DROP DATABASE {db}";
            await command.ExecuteNonQueryAsync();
        }
    }
}
