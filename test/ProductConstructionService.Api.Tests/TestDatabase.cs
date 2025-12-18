// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Maestro.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace ProductConstructionService.Api.Tests;

public class TestDatabase : IDisposable
{
    private readonly string _testDatabasePrefix;
    internal const string TestNamespace = "test-namespace";
    private readonly Lazy<Task<string>> _databaseName;

    public TestDatabase(string testDatabasePrefix)
    {
        _testDatabasePrefix = testDatabasePrefix;
        _databaseName = new(InitializeDatabaseAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public void Dispose()
    {
        using var connection = new SqlConnection(BuildAssetRegistryContextFactory.GetConnectionString("master"));
        connection.Open();
        DropAllTestDatabases(connection).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    public async Task<string> GetConnectionString() => BuildAssetRegistryContextFactory.GetConnectionString(await _databaseName.Value);

    private async Task<string> InitializeDatabaseAsync()
    {
        string databaseName = $"{_testDatabasePrefix}_{DateTime.Now:yyyyMMddHHmmss}";

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
        var dbContext = provider.GetRequiredService<BuildAssetRegistryContext>();
        await dbContext.Database.MigrateAsync();
        await dbContext.Namespaces.AddAsync(new Maestro.Data.Models.Namespace
        {
            Name = TestNamespace
        });
        await dbContext.SaveChangesAsync();

        return databaseName;
    }

    private async Task DropAllTestDatabases(SqlConnection connection)
    {
        var previousTestDbs = new List<string>();
        await using (SqlCommand command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT name FROM sys.databases WHERE name LIKE '{_testDatabasePrefix}%'";
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
