// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace ProductConstructionService.Api.Tests;

[SetUpFixture]
public static class SharedData
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

    private class SharedTestDatabase : TestDatabase
    {
    }
}

public class TestDatabase : IDisposable
{
    private const string TestDatabasePrefix = "TFD_";
    private string _databaseName = null!;
    private readonly SemaphoreSlim _createLock = new(1);

    protected TestDatabase()
    {
    }

    public void Dispose()
    {
        using var connection = new SqlConnection(BuildAssetRegistryContextFactory.GetConnectionString("master"));
        connection.Open();
        DropAllTestDatabases(connection).GetAwaiter().GetResult();
    }

    public async Task<string> GetConnectionString()
    {
        if (_databaseName != null)
        {
            return ConnectionString;
        }

        await _createLock.WaitAsync();
        try
        {
            var databaseName = $"{TestDatabasePrefix}_{TestContext.CurrentContext.Test.ClassName!.Split('.').Last()}_{TestContext.CurrentContext.Test.MethodName}_{DateTime.Now:yyyyMMddHHmmss}";
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
            { EnvironmentName = Environments.Development });
            collection.AddBuildAssetRegistry(o =>
            {
                o.UseSqlServer(BuildAssetRegistryContextFactory.GetConnectionString(databaseName));
                o.EnableServiceProviderCaching(false);
            });

            await using ServiceProvider provider = collection.BuildServiceProvider();
            await provider.GetRequiredService<BuildAssetRegistryContext>().Database.MigrateAsync();

            _databaseName = databaseName;
            return ConnectionString;
        }
        finally
        {
            _createLock.Dispose();
        }
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

        foreach (var db in previousTestDbs)
        {
            TestContext.WriteLine($"Dropping test database '{db}'");
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE {db} SET single_user with rollback immediate; DROP DATABASE {db}";
            await command.ExecuteNonQueryAsync();
        }
    }

    private string ConnectionString => BuildAssetRegistryContextFactory.GetConnectionString(_databaseName);
}
